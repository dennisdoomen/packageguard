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
            cached = await LoadAsync(repositoryApiRoot);

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
        package.HasReadme = cached.HasReadme;
        package.HasDefaultReadme = cached.HasDefaultReadme;
        package.HasContributingGuide = cached.HasContributingGuide;
        package.HasSecurityPolicy = cached.HasSecurityPolicy;
        package.OpenBugIssueCount = cached.OpenBugIssueCount;
        package.StaleCriticalBugIssueCount = cached.StaleCriticalBugIssueCount;
        package.MedianIssueResponseDays = cached.MedianIssueResponseDays;
        package.MedianPullRequestMergeDays = cached.MedianPullRequestMergeDays;
        package.PublishedAt ??= cached.LastReleaseAt;
    }

    private async Task<GitHubRepositoryRiskData?> LoadAsync(string repositoryApiRoot)
    {
        try
        {
            using JsonDocument repoDocument = await GetJsonAsync(repositoryApiRoot);
            JsonElement repo = repoDocument.RootElement;

            string defaultBranch = repo.TryGetProperty("default_branch", out JsonElement defaultBranchElement)
                ? defaultBranchElement.GetString() ?? "main"
                : "main";

            string ownerLogin = repo.GetProperty("owner").GetProperty("login").GetString() ?? string.Empty;
            bool ownerIsOrganization = string.Equals(repo.GetProperty("owner").GetProperty("type").GetString(),
                "Organization", StringComparison.OrdinalIgnoreCase);

            using JsonDocument ownerDocument = await GetJsonAsync(ownerIsOrganization
                ? $"https://api.github.com/orgs/{ownerLogin}"
                : $"https://api.github.com/users/{ownerLogin}");

            DateTimeOffset? ownerCreatedAt = TryReadDate(ownerDocument.RootElement, "created_at");
            DateTimeOffset? lastReleaseAt = await TryGetLastReleaseDateAsync(repositoryApiRoot);
            GitHubReadmeData readmeData = await TryGetReadmeDataAsync(repositoryApiRoot);
            string[] rootFiles = await GetRootFilesAsync(repositoryApiRoot, defaultBranch);
            GitHubIssueData issueData = await GetIssueDataAsync(repositoryApiRoot);
            int contributorCount = await GetContributorCountAsync(repositoryApiRoot);
            double? medianPullRequestMergeDays = await GetMedianPullRequestMergeDaysAsync(repositoryApiRoot);

            return new GitHubRepositoryRiskData
            {
                OwnerIsOrganization = ownerIsOrganization,
                OwnerCreatedAt = ownerCreatedAt,
                ContributorCount = contributorCount,
                HasReadme = readmeData.Exists,
                HasDefaultReadme = readmeData.IsDefault,
                HasContributingGuide = rootFiles.Contains("CONTRIBUTING.md", StringComparer.OrdinalIgnoreCase),
                HasSecurityPolicy = rootFiles.Contains("SECURITY.md", StringComparer.OrdinalIgnoreCase),
                OpenBugIssueCount = issueData.OpenBugIssueCount,
                StaleCriticalBugIssueCount = issueData.StaleCriticalBugIssueCount,
                MedianIssueResponseDays = issueData.MedianIssueResponseDays,
                MedianPullRequestMergeDays = medianPullRequestMergeDays,
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

    private async Task<int> GetContributorCountAsync(string repositoryApiRoot)
    {
        using JsonDocument contributorsDoc = await GetJsonAsync($"{repositoryApiRoot}/contributors?per_page=100");
        if (contributorsDoc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return contributorsDoc.RootElement.EnumerateArray()
            .Count(contributor =>
            {
                string login = contributor.TryGetProperty("login", out JsonElement loginElement)
                    ? loginElement.GetString() ?? string.Empty
                    : string.Empty;

                return !login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) &&
                       !login.Contains("dependabot", StringComparison.OrdinalIgnoreCase) &&
                       !login.Contains("copilot", StringComparison.OrdinalIgnoreCase);
            });
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

        public bool HasReadme { get; init; }

        public bool HasDefaultReadme { get; init; }

        public bool HasContributingGuide { get; init; }

        public bool HasSecurityPolicy { get; init; }

        public int OpenBugIssueCount { get; init; }

        public int StaleCriticalBugIssueCount { get; init; }

        public double? MedianIssueResponseDays { get; init; }

        public double? MedianPullRequestMergeDays { get; init; }

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
}
