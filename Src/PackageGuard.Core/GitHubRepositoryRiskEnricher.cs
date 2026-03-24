using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

internal sealed class GitHubRepositoryRiskEnricher(ILogger logger, string? gitHubApiKey) : IEnrichPackageRisk
{
    private static readonly HttpClient HttpClient = new();
    private static readonly Dictionary<string, GitHubRepositoryRiskData?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock CacheLock = new();

    static GitHubRepositoryRiskEnricher()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PackageGuard", "v1"));
    }

    public async Task EnrichAsync(PackageInfo package)
    {
        string? repositoryApiRoot = GetGitHubApiRoot(package.RepositoryUrl);
        if (repositoryApiRoot is null)
        {
            return;
        }

        GitHubRepositoryRiskData? cached;

        lock (CacheLock)
        {
            Cache.TryGetValue(repositoryApiRoot, out cached);
        }

        if (cached is null)
        {
            cached = await LoadAsync(repositoryApiRoot, package.RepositoryUrl);

            lock (CacheLock)
            {
                Cache[repositoryApiRoot] = cached;
            }
        }

        if (cached is null)
        {
            return;
        }

        package.OwnerIsOrganization = cached.OwnerIsOrganization;
        package.OwnerCreatedAt = cached.OwnerCreatedAt;
        package.ContributorCount = cached.ContributorCount;
        package.TopContributorShare = cached.TopContributorShare;
        package.TopTwoContributorShare = cached.TopTwoContributorShare;
        package.HasReadme = cached.HasReadme;
        package.HasDefaultReadme = cached.HasDefaultReadme;
        package.HasContributingGuide = cached.HasContributingGuide;
        package.HasSecurityPolicy = cached.HasSecurityPolicy;
        package.HasChangelog = cached.HasChangelog;
        package.HasDefaultChangelog = cached.HasDefaultChangelog;
        package.OpenBugIssueCount = cached.OpenBugIssueCount;
        package.StaleCriticalBugIssueCount = cached.StaleCriticalBugIssueCount;
        package.MedianIssueResponseDays = cached.MedianIssueResponseDays;
        package.MedianPullRequestMergeDays = cached.MedianPullRequestMergeDays;
        package.RecentFailedWorkflowCount = cached.RecentFailedWorkflowCount;
        package.HasRecentSuccessfulWorkflowRun = cached.HasRecentSuccessfulWorkflowRun;
        package.OpenSsfScore = cached.OpenSsfScore;
        package.HasBranchProtection = cached.HasBranchProtection;
        package.HasProvenanceAttestation = cached.HasProvenanceAttestation;
        package.HasRepositoryOwnershipOrRenameChurn = cached.HasRepositoryOwnershipOrRenameChurn;
        package.PublishedAt ??= cached.LastReleaseAt;
    }

    private async Task<GitHubRepositoryRiskData?> LoadAsync(string repositoryApiRoot, string? declaredRepositoryUrl)
    {
        try
        {
            using JsonDocument repoDocument = await GetJsonAsync(repositoryApiRoot);
            JsonElement repo = repoDocument.RootElement;

            string defaultBranch = repo.TryGetProperty("default_branch", out JsonElement defaultBranchElement)
                ? defaultBranchElement.GetString() ?? "main"
                : "main";

            string ownerLogin = repo.GetProperty("owner").GetProperty("login").GetString() ?? string.Empty;
            string repositoryName = repo.GetProperty("name").GetString() ?? string.Empty;
            bool ownerIsOrganization = string.Equals(repo.GetProperty("owner").GetProperty("type").GetString(),
                "Organization", StringComparison.OrdinalIgnoreCase);
            string canonicalUrl = repo.TryGetProperty("html_url", out JsonElement htmlUrlElement)
                ? htmlUrlElement.GetString() ?? string.Empty
                : string.Empty;

            using JsonDocument ownerDocument = await GetJsonAsync(ownerIsOrganization
                ? $"https://api.github.com/orgs/{ownerLogin}"
                : $"https://api.github.com/users/{ownerLogin}");

            DateTimeOffset? ownerCreatedAt = TryReadDate(ownerDocument.RootElement, "created_at");
            DateTimeOffset? lastReleaseAt = await TryGetLastReleaseDateAsync(repositoryApiRoot);
            GitHubReadmeData readmeData = await TryGetReadmeDataAsync(repositoryApiRoot);
            string[] rootFiles = await GetRootFilesAsync(repositoryApiRoot, defaultBranch);
            GitHubIssueData issueData = await GetIssueDataAsync(repositoryApiRoot);
            GitHubContributorData contributorData = await GetContributorDataAsync(repositoryApiRoot);
            double? medianPullRequestMergeDays = await GetMedianPullRequestMergeDaysAsync(repositoryApiRoot);
            GitHubWorkflowData workflowData = await GetWorkflowDataAsync(repositoryApiRoot, defaultBranch);
            bool? hasBranchProtection = await TryGetBranchProtectionAsync(repositoryApiRoot, defaultBranch);
            GitHubChangelogData changelogData = await GetChangelogDataAsync(repositoryApiRoot, defaultBranch, rootFiles);
            GitHubScorecardData scorecardData = await TryGetScorecardDataAsync(ownerLogin, repositoryName);
            bool hasProvenanceAttestation = rootFiles.Contains(".github", StringComparer.OrdinalIgnoreCase) &&
                                           await HasProvenanceAttestationAsync(repositoryApiRoot, defaultBranch);
            bool hasRepositoryOwnershipOrRenameChurn = HasRepositoryOwnershipOrRenameChurn(declaredRepositoryUrl, canonicalUrl);

            return new GitHubRepositoryRiskData
            {
                OwnerIsOrganization = ownerIsOrganization,
                OwnerCreatedAt = ownerCreatedAt,
                ContributorCount = contributorData.ContributorCount,
                TopContributorShare = contributorData.TopContributorShare,
                TopTwoContributorShare = contributorData.TopTwoContributorShare,
                HasReadme = readmeData.Exists,
                HasDefaultReadme = readmeData.IsDefault,
                HasContributingGuide = rootFiles.Contains("CONTRIBUTING.md", StringComparer.OrdinalIgnoreCase),
                HasSecurityPolicy = rootFiles.Contains("SECURITY.md", StringComparer.OrdinalIgnoreCase),
                HasChangelog = changelogData.Exists,
                HasDefaultChangelog = changelogData.IsDefault,
                OpenBugIssueCount = issueData.OpenBugIssueCount,
                StaleCriticalBugIssueCount = issueData.StaleCriticalBugIssueCount,
                MedianIssueResponseDays = issueData.MedianIssueResponseDays,
                MedianPullRequestMergeDays = medianPullRequestMergeDays,
                RecentFailedWorkflowCount = workflowData.RecentFailedWorkflowCount,
                HasRecentSuccessfulWorkflowRun = workflowData.HasRecentSuccessfulWorkflowRun,
                OpenSsfScore = scorecardData.Score,
                HasBranchProtection = hasBranchProtection ?? scorecardData.HasBranchProtection,
                HasProvenanceAttestation = hasProvenanceAttestation,
                HasRepositoryOwnershipOrRenameChurn = hasRepositoryOwnershipOrRenameChurn,
                LastReleaseAt = lastReleaseAt
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug("Failed to fetch GitHub repository risk metadata from {RepositoryApiRoot}: {Error}",
                repositoryApiRoot, ex.Message);
            return null;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!string.IsNullOrWhiteSpace(gitHubApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gitHubApiKey);
        }

        using HttpResponseMessage response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<string[]> GetRootFilesAsync(string repositoryApiRoot, string defaultBranch)
    {
        using JsonDocument contents = await GetJsonAsync($"{repositoryApiRoot}/contents?ref={Uri.EscapeDataString(defaultBranch)}");
        if (contents.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return contents.RootElement.EnumerateArray()
            .Select(item => item.TryGetProperty("name", out JsonElement name) ? name.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    private async Task<GitHubReadmeData> TryGetReadmeDataAsync(string repositoryApiRoot)
    {
        try
        {
            using JsonDocument readmeDoc = await GetJsonAsync($"{repositoryApiRoot}/readme");
            string content = string.Empty;

            if (readmeDoc.RootElement.TryGetProperty("content", out JsonElement contentElement))
            {
                string? encoded = contentElement.GetString();
                if (!string.IsNullOrWhiteSpace(encoded))
                {
                    byte[] bytes = Convert.FromBase64String(encoded.Replace("\n", string.Empty));
                    content = Encoding.UTF8.GetString(bytes);
                }
            }

            return new GitHubReadmeData
            {
                Exists = true,
                IsDefault = LooksLikeBoilerplateReadme(content)
            };
        }
        catch
        {
            return new GitHubReadmeData { Exists = false };
        }
    }

    private async Task<GitHubContributorData> GetContributorDataAsync(string repositoryApiRoot)
    {
        using JsonDocument contributorsDoc = await GetJsonAsync($"{repositoryApiRoot}/contributors?per_page=100");
        if (contributorsDoc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new GitHubContributorData();
        }

        int[] contributionCounts = contributorsDoc.RootElement.EnumerateArray()
            .Where(contributor =>
            {
                string login = contributor.TryGetProperty("login", out JsonElement loginElement)
                    ? loginElement.GetString() ?? string.Empty
                    : string.Empty;

                return !login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) &&
                       !login.Contains("dependabot", StringComparison.OrdinalIgnoreCase) &&
                       !login.Contains("copilot", StringComparison.OrdinalIgnoreCase);
            })
            .Select(contributor => contributor.TryGetProperty("contributions", out JsonElement contributionsElement) &&
                                   contributionsElement.TryGetInt32(out int contributions)
                ? contributions
                : 0)
            .OrderByDescending(contributions => contributions)
            .ToArray();

        double totalContributions = contributionCounts.Sum();
        return new GitHubContributorData
        {
            ContributorCount = contributionCounts.Length,
            TopContributorShare = totalContributions > 0 ? contributionCounts.FirstOrDefault() / totalContributions : null,
            TopTwoContributorShare = totalContributions > 0 ? contributionCounts.Take(2).Sum() / totalContributions : null
        };
    }

    private async Task<GitHubIssueData> GetIssueDataAsync(string repositoryApiRoot)
    {
        using JsonDocument issuesDoc = await GetJsonAsync($"{repositoryApiRoot}/issues?state=open&labels=bug&per_page=100");
        if (issuesDoc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new GitHubIssueData();
        }

        List<double> responseDays = [];
        int bugCount = 0;
        int staleCriticalBugCount = 0;

        foreach (JsonElement issue in issuesDoc.RootElement.EnumerateArray())
        {
            if (issue.TryGetProperty("pull_request", out _))
            {
                continue;
            }

            bugCount++;
            DateTimeOffset createdAt = TryReadDate(issue, "created_at") ?? DateTimeOffset.UtcNow;

            bool isCritical = issue.TryGetProperty("labels", out JsonElement labels) &&
                              labels.ValueKind == JsonValueKind.Array &&
                              labels.EnumerateArray().Any(label =>
                              {
                                  string name = label.TryGetProperty("name", out JsonElement nameElement)
                                      ? nameElement.GetString() ?? string.Empty
                                      : string.Empty;
                                  return name.Contains("critical", StringComparison.OrdinalIgnoreCase);
                              });

            if (isCritical && createdAt < DateTimeOffset.UtcNow.AddMonths(-6))
            {
                staleCriticalBugCount++;
            }

            string commentsUrl = issue.TryGetProperty("comments_url", out JsonElement commentsElement)
                ? commentsElement.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(commentsUrl))
            {
                continue;
            }

            try
            {
                using JsonDocument commentsDoc = await GetJsonAsync(commentsUrl);
                if (commentsDoc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                DateTimeOffset? firstMaintainerComment = commentsDoc.RootElement.EnumerateArray()
                    .Where(comment => comment.TryGetProperty("author_association", out JsonElement associationElement) &&
                                      IsMaintainerAssociation(associationElement.GetString()))
                    .Select(comment => TryReadDate(comment, "created_at"))
                    .Where(date => date is not null)
                    .OrderBy(date => date)
                    .FirstOrDefault();

                if (firstMaintainerComment is DateTimeOffset firstResponse)
                {
                    responseDays.Add((firstResponse - createdAt).TotalDays);
                }
            }
            catch
            {
            }
        }

        return new GitHubIssueData
        {
            OpenBugIssueCount = bugCount,
            StaleCriticalBugIssueCount = staleCriticalBugCount,
            MedianIssueResponseDays = ComputeMedian(responseDays)
        };
    }

    private async Task<double?> GetMedianPullRequestMergeDaysAsync(string repositoryApiRoot)
    {
        using JsonDocument pullsDoc = await GetJsonAsync($"{repositoryApiRoot}/pulls?state=closed&sort=updated&direction=desc&per_page=100");
        if (pullsDoc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<double> mergeDays = pullsDoc.RootElement.EnumerateArray()
            .Where(pr => pr.TryGetProperty("merged_at", out JsonElement mergedAtElement) &&
                         mergedAtElement.ValueKind == JsonValueKind.String)
            .Select(pr =>
            {
                DateTimeOffset? createdAt = TryReadDate(pr, "created_at");
                DateTimeOffset? mergedAt = TryReadDate(pr, "merged_at");
                return createdAt is not null && mergedAt is not null ? (mergedAt.Value - createdAt.Value).TotalDays : (double?)null;
            })
            .Where(days => days is not null)
            .Select(days => days!.Value)
            .ToList();

        return ComputeMedian(mergeDays);
    }

    private async Task<DateTimeOffset?> TryGetLastReleaseDateAsync(string repositoryApiRoot)
    {
        try
        {
            using JsonDocument releaseDoc = await GetJsonAsync($"{repositoryApiRoot}/releases?per_page=5");
            if (releaseDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return releaseDoc.RootElement.EnumerateArray()
                .Select(release => TryReadDate(release, "published_at") ?? TryReadDate(release, "created_at"))
                .Where(date => date is not null)
                .OrderByDescending(date => date)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private async Task<GitHubWorkflowData> GetWorkflowDataAsync(string repositoryApiRoot, string defaultBranch)
    {
        try
        {
            using JsonDocument workflowsDoc = await GetJsonAsync(
                $"{repositoryApiRoot}/actions/runs?branch={Uri.EscapeDataString(defaultBranch)}&per_page=10");

            if (!workflowsDoc.RootElement.TryGetProperty("workflow_runs", out JsonElement runs) ||
                runs.ValueKind != JsonValueKind.Array)
            {
                return new GitHubWorkflowData();
            }

            JsonElement[] workflowRuns = runs.EnumerateArray().ToArray();
            return new GitHubWorkflowData
            {
                RecentFailedWorkflowCount = workflowRuns.Count(run =>
                    string.Equals(run.TryGetProperty("conclusion", out JsonElement conclusion) ? conclusion.GetString() : null,
                        "failure", StringComparison.OrdinalIgnoreCase)),
                HasRecentSuccessfulWorkflowRun = workflowRuns.Any(run =>
                    string.Equals(run.TryGetProperty("conclusion", out JsonElement conclusion) ? conclusion.GetString() : null,
                        "success", StringComparison.OrdinalIgnoreCase))
            };
        }
        catch
        {
            return new GitHubWorkflowData();
        }
    }

    private async Task<bool?> TryGetBranchProtectionAsync(string repositoryApiRoot, string defaultBranch)
    {
        try
        {
            using JsonDocument branchDoc = await GetJsonAsync(
                $"{repositoryApiRoot}/branches/{Uri.EscapeDataString(defaultBranch)}");

            if (branchDoc.RootElement.TryGetProperty("protected", out JsonElement protectedElement) &&
                protectedElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return protectedElement.GetBoolean();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<GitHubChangelogData> GetChangelogDataAsync(string repositoryApiRoot, string defaultBranch, string[] rootFiles)
    {
        string? changelogFile = rootFiles.FirstOrDefault(file =>
            file.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("CHANGELOG", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("RELEASE_NOTES.md", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("NEWS.md", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(changelogFile))
        {
            return new GitHubChangelogData();
        }

        string content = await TryGetFileContentAsync(repositoryApiRoot, changelogFile, defaultBranch);
        return new GitHubChangelogData
        {
            Exists = true,
            IsDefault = LooksLikeBoilerplateChangelog(content)
        };
    }

    private async Task<GitHubScorecardData> TryGetScorecardDataAsync(string owner, string repo)
    {
        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(
                $"https://api.securityscorecards.dev/projects/github.com/{owner}/{repo}");
            response.EnsureSuccessStatusCode();

            using JsonDocument scorecardDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = scorecardDoc.RootElement;

            bool? hasBranchProtection = null;
            if (root.TryGetProperty("checks", out JsonElement checks) && checks.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement check in checks.EnumerateArray())
                {
                    string? name = check.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : null;
                    if (!string.Equals(name, "Branch-Protection", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (check.TryGetProperty("score", out JsonElement scoreElement) && scoreElement.TryGetDouble(out double branchScore))
                    {
                        hasBranchProtection = branchScore > 0;
                    }
                }
            }

            return new GitHubScorecardData
            {
                Score = root.TryGetProperty("score", out JsonElement score) && score.TryGetDouble(out double value) ? value : null,
                HasBranchProtection = hasBranchProtection
            };
        }
        catch
        {
            return new GitHubScorecardData();
        }
    }

    private async Task<bool> HasProvenanceAttestationAsync(string repositoryApiRoot, string defaultBranch)
    {
        try
        {
            using JsonDocument workflowsDoc = await GetJsonAsync(
                $"{repositoryApiRoot}/contents/.github/workflows?ref={Uri.EscapeDataString(defaultBranch)}");

            if (workflowsDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return workflowsDoc.RootElement.EnumerateArray().Any(item =>
            {
                string name = item.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                string lowered = name.ToLowerInvariant();
                return lowered.Contains("slsa") || lowered.Contains("provenance") || lowered.Contains("attest");
            });
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> TryGetFileContentAsync(string repositoryApiRoot, string path, string defaultBranch)
    {
        try
        {
            using JsonDocument fileDoc = await GetJsonAsync(
                $"{repositoryApiRoot}/contents/{Uri.EscapeDataString(path)}?ref={Uri.EscapeDataString(defaultBranch)}");

            if (fileDoc.RootElement.TryGetProperty("content", out JsonElement contentElement))
            {
                string? encoded = contentElement.GetString();
                if (!string.IsNullOrWhiteSpace(encoded))
                {
                    byte[] bytes = Convert.FromBase64String(encoded.Replace("\n", string.Empty));
                    return Encoding.UTF8.GetString(bytes);
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string? GetGitHubApiRoot(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return null;
        }

        Match match = Regex.Match(repositoryUrl,
            @"github\.com/(?<owner>[a-zA-Z0-9._-]+)/(?<repo>[a-zA-Z0-9._-]+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        string owner = match.Groups["owner"].Value;
        string repo = match.Groups["repo"].Value.TrimEnd('.');

        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }

        return $"https://api.github.com/repos/{owner}/{repo}";
    }

    private static bool LooksLikeBoilerplateReadme(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        string normalized = content.ToLowerInvariant();
        return content.Length < 400 ||
               normalized.Contains("todo") ||
               normalized.Contains("coming soon") ||
               normalized.Contains("add a description");
    }

    private static bool LooksLikeBoilerplateChangelog(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        string normalized = content.ToLowerInvariant();
        return content.Length < 200 ||
               normalized.Contains("coming soon") ||
               normalized.Contains("todo");
    }

    private static bool HasRepositoryOwnershipOrRenameChurn(string? declaredRepositoryUrl, string canonicalUrl)
    {
        string? declaredIdentifier = TryGetGitHubIdentifier(declaredRepositoryUrl);
        string? canonicalIdentifier = TryGetGitHubIdentifier(canonicalUrl);
        return !string.IsNullOrWhiteSpace(canonicalIdentifier) &&
               !string.IsNullOrWhiteSpace(declaredIdentifier) &&
               !canonicalIdentifier.Equals(declaredIdentifier, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetGitHubIdentifier(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return null;
        }

        Match match = Regex.Match(repositoryUrl,
            @"github\.com/(?<owner>[a-zA-Z0-9._-]+)/(?<repo>[a-zA-Z0-9._-]+)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return $"{match.Groups["owner"].Value}/{match.Groups["repo"].Value.TrimEnd('.').Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase)}";
    }

    private static DateTimeOffset? TryReadDate(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property) &&
            DateTimeOffset.TryParse(property.GetString(), out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool IsMaintainerAssociation(string? association)
    {
        return association is "OWNER" or "MEMBER" or "COLLABORATOR";
    }

    private static double? ComputeMedian(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2.0
            : values[middle];
    }

    private sealed class GitHubRepositoryRiskData
    {
        public bool OwnerIsOrganization { get; init; }

        public DateTimeOffset? OwnerCreatedAt { get; init; }

        public int ContributorCount { get; init; }

        public double? TopContributorShare { get; init; }

        public double? TopTwoContributorShare { get; init; }

        public bool HasReadme { get; init; }

        public bool HasDefaultReadme { get; init; }

        public bool HasContributingGuide { get; init; }

        public bool HasSecurityPolicy { get; init; }

        public bool HasChangelog { get; init; }

        public bool HasDefaultChangelog { get; init; }

        public int OpenBugIssueCount { get; init; }

        public int StaleCriticalBugIssueCount { get; init; }

        public double? MedianIssueResponseDays { get; init; }

        public double? MedianPullRequestMergeDays { get; init; }

        public int? RecentFailedWorkflowCount { get; init; }

        public bool? HasRecentSuccessfulWorkflowRun { get; init; }

        public double? OpenSsfScore { get; init; }

        public bool? HasBranchProtection { get; init; }

        public bool? HasProvenanceAttestation { get; init; }

        public bool? HasRepositoryOwnershipOrRenameChurn { get; init; }

        public DateTimeOffset? LastReleaseAt { get; init; }
    }

    private sealed class GitHubIssueData
    {
        public int OpenBugIssueCount { get; init; }

        public int StaleCriticalBugIssueCount { get; init; }

        public double? MedianIssueResponseDays { get; init; }
    }

    private sealed class GitHubReadmeData
    {
        public bool Exists { get; init; }

        public bool IsDefault { get; init; }
    }

    private sealed class GitHubChangelogData
    {
        public bool Exists { get; init; }

        public bool IsDefault { get; init; }
    }

    private sealed class GitHubContributorData
    {
        public int ContributorCount { get; init; }

        public double? TopContributorShare { get; init; }

        public double? TopTwoContributorShare { get; init; }
    }

    private sealed class GitHubWorkflowData
    {
        public int? RecentFailedWorkflowCount { get; init; }

        public bool? HasRecentSuccessfulWorkflowRun { get; init; }
    }

    private sealed class GitHubScorecardData
    {
        public double? Score { get; init; }

        public bool? HasBranchProtection { get; init; }
    }
}
