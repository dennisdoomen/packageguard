using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

/// <summary>Enriches package risk data using GitHub repository API metadata.</summary>
internal sealed class GitHubRepositoryRiskEnricher(ILogger logger, string? gitHubApiKey) : IEnrichPackageRisk
{
    /// <summary>Shared HTTP client for GitHub API requests.</summary>
    private static readonly HttpClient HttpClient = new();

    /// <summary>Cache of repository API root URL to risk data.</summary>
    private static readonly Dictionary<string, GitHubRepositoryRiskData?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>In-flight load tasks per repository API root, preventing duplicate concurrent fetches.</summary>
    private static readonly Dictionary<string, Task<GitHubRepositoryRiskData?>> InFlightLoads = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lock for thread-safe cache and in-flight loads access.</summary>
    private static readonly Lock CacheLock = new();

    /// <summary>Static constructor that sets the User-Agent header.</summary>
    static GitHubRepositoryRiskEnricher()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PackageGuard", "v1"));
    }

    /// <summary>Returns true if GitHub risk data is already populated for the package.</summary>
    public bool HasCachedData(PackageInfo package) => package.HasGitHubRiskData;

    /// <summary>Fetches GitHub repository risk data and applies it to the package.</summary>
    public async Task EnrichAsync(PackageInfo package)
    {
        string? repositoryApiRoot = GetGitHubApiRoot(package.RepositoryUrl);
        if (repositoryApiRoot is null)
        {
            return;
        }

        GitHubRepositoryRiskData? cached = await GetRepositoryRiskDataAsync(repositoryApiRoot);
        if (cached is null)
        {
            return;
        }

        package.OwnerIsOrganization = cached.OwnerIsOrganization;
        package.OwnerCreatedAt = cached.OwnerCreatedAt;
        package.ContributorCount = cached.ContributorCount;
        package.TopContributorShare = cached.TopContributorShare;
        package.TopTwoContributorShare = cached.TopTwoContributorShare;
        package.RecentMaintainerCount = cached.RecentMaintainerCount;
        package.HasReadme = cached.HasReadme;
        package.HasDefaultReadme = cached.HasDefaultReadme;
        package.ReadmeUpdatedAt = cached.ReadmeUpdatedAt;
        package.HasContributingGuide = cached.HasContributingGuide;
        package.HasSecurityPolicy = cached.HasSecurityPolicy;
        package.HasDetailedSecurityPolicy = cached.HasDetailedSecurityPolicy;
        package.HasCoordinatedDisclosure = cached.HasCoordinatedDisclosure;
        package.HasChangelog = cached.HasChangelog;
        package.HasDefaultChangelog = cached.HasDefaultChangelog;
        package.ChangelogUpdatedAt = cached.ChangelogUpdatedAt;
        package.OpenBugIssueCount = cached.OpenBugIssueCount;
        package.StaleCriticalBugIssueCount = cached.StaleCriticalBugIssueCount;
        package.MedianIssueResponseDays = cached.MedianIssueResponseDays;
        package.MedianCriticalIssueResponseDays = cached.MedianCriticalIssueResponseDays;
        package.IssueResponseCoverage = cached.IssueResponseCoverage;
        package.MedianOpenBugAgeDays = cached.MedianOpenBugAgeDays;
        package.ClosedBugIssueCountLast90Days = cached.ClosedBugIssueCountLast90Days;
        package.ReopenedBugIssueCountLast90Days = cached.ReopenedBugIssueCountLast90Days;
        package.IssueTriageWithinSevenDaysRate = cached.IssueTriageWithinSevenDaysRate;
        package.MedianPullRequestMergeDays = cached.MedianPullRequestMergeDays;
        package.ExternalContributionRate = cached.ExternalContributionRate;
        package.UniqueReviewerCount = cached.UniqueReviewerCount;
        package.ReviewerDiversityRatio = cached.ReviewerDiversityRatio;
        package.RecentFailedWorkflowCount = cached.RecentFailedWorkflowCount;
        package.HasRecentSuccessfulWorkflowRun = cached.HasRecentSuccessfulWorkflowRun;
        package.WorkflowFailureRate = cached.WorkflowFailureRate;
        package.HasFlakyWorkflowPattern = cached.HasFlakyWorkflowPattern;
        package.RequiredStatusCheckCount = cached.RequiredStatusCheckCount;
        package.WorkflowPlatformCount = cached.WorkflowPlatformCount;
        package.HasCoverageWorkflowSignal = cached.HasCoverageWorkflowSignal;
        package.HasReproducibleBuildSignal = cached.HasReproducibleBuildSignal;
        package.HasDependencyUpdateAutomation = cached.HasDependencyUpdateAutomation;
        package.HasTestSignal = cached.HasTestSignal;
        package.OpenSsfScore = cached.OpenSsfScore;
        package.HasBranchProtection = cached.HasBranchProtection;
        package.HasProvenanceAttestation = cached.HasProvenanceAttestation;
        package.HasRepositoryOwnershipOrRenameChurn =
            HasRepositoryOwnershipOrRenameChurn(package.RepositoryUrl, cached.CanonicalUrl);
        package.HasVerifiedReleaseSignature = cached.HasVerifiedReleaseSignature;
        package.HasVerifiedPublisher ??= cached.HasVerifiedPublisher;
        package.HasReleaseNotes = cached.HasReleaseNotes;
        package.HasSemVerReleaseTags = cached.HasSemVerReleaseTags;
        package.MeanReleaseIntervalDays = cached.MeanReleaseIntervalDays;
        package.MajorReleaseRatio = cached.MajorReleaseRatio;
        package.PrereleaseRatio = cached.PrereleaseRatio;
        package.RapidReleaseCorrectionCount = cached.RapidReleaseCorrectionCount;
        package.VerifiedCommitRatio = cached.VerifiedCommitRatio;
        package.MedianMaintainerActivityDays = cached.MedianMaintainerActivityDays;
        package.PublishedAt ??= cached.LastReleaseAt;
        package.HasGitHubRiskData = true;
    }

    /// <summary>Returns cached or loads and caches risk data for the given API root.</summary>
    private async Task<GitHubRepositoryRiskData?> GetRepositoryRiskDataAsync(string repositoryApiRoot)
    {
        Task<GitHubRepositoryRiskData?> loadTask;

        lock (CacheLock)
        {
            if (Cache.TryGetValue(repositoryApiRoot, out GitHubRepositoryRiskData? cached) && cached != null)
            {
                return cached;
            }

            if (!InFlightLoads.TryGetValue(repositoryApiRoot, out loadTask!))
            {
                loadTask = LoadAsync(repositoryApiRoot);
                InFlightLoads[repositoryApiRoot] = loadTask;
            }
        }

        GitHubRepositoryRiskData? loaded = await loadTask;

        lock (CacheLock)
        {
            InFlightLoads.Remove(repositoryApiRoot);

            if (loaded != null)
            {
                Cache[repositoryApiRoot] = loaded;
            }
        }

        return loaded;
    }

    /// <summary>Fetches all GitHub data for the repository and assembles a GitHubRepositoryRiskData record.</summary>
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

            Task<GitHubReleaseData> releaseDataTask = GetReleaseDataAsync(repositoryApiRoot);
            Task<GitHubReadmeData> readmeTask = TryGetReadmeDataAsync(repositoryApiRoot);
            Task<string[]> rootFilesTask = GetRootFilesAsync(repositoryApiRoot, defaultBranch);
            Task<GitHubIssueData> issueDataTask = GetIssueDataAsync(repositoryApiRoot);
            Task<GitHubContributorData> contributorDataTask = GetContributorDataAsync(repositoryApiRoot);
            Task<GitHubCommitHealthData> commitHealthTask = GetCommitHealthDataAsync(repositoryApiRoot, defaultBranch);
            Task<double?> medianPullRequestMergeDaysTask = GetMedianPullRequestMergeDaysAsync(repositoryApiRoot);
            Task<GitHubPullRequestQualityData> pullRequestQualityTask = GetPullRequestQualityDataAsync(repositoryApiRoot);
            Task<GitHubWorkflowData> workflowDataTask = GetWorkflowDataAsync(repositoryApiRoot, defaultBranch);
            Task<GitHubBranchProtectionData> branchProtectionTask = GetBranchProtectionDataAsync(repositoryApiRoot, defaultBranch);
            Task<GitHubScorecardData> scorecardTask = TryGetScorecardDataAsync(ownerLogin, repositoryName);

            string[] rootFiles = await rootFilesTask;
            Task<GitHubChangelogData> changelogTask = GetChangelogDataAsync(repositoryApiRoot, defaultBranch, rootFiles);
            Task<GitHubSecurityPolicyData> securityPolicyTask = GetSecurityPolicyDataAsync(repositoryApiRoot, defaultBranch, rootFiles);
            Task<GitHubWorkflowFileSignals> workflowFileSignalsTask = rootFiles.Contains(".github", StringComparer.OrdinalIgnoreCase)
                ? GetWorkflowFileSignalsAsync(repositoryApiRoot, defaultBranch, rootFiles)
                : Task.FromResult(new GitHubWorkflowFileSignals());
            string? readmeFileName = rootFiles.FirstOrDefault(file => file.StartsWith("README", StringComparison.OrdinalIgnoreCase));
            Task<DateTimeOffset?> readmeUpdatedTask = string.IsNullOrWhiteSpace(readmeFileName)
                ? Task.FromResult<DateTimeOffset?>(null)
                : TryGetLatestCommitDateAsync(repositoryApiRoot, readmeFileName, defaultBranch);
            Task<DateTimeOffset?> changelogUpdatedTask = rootFiles.Any(IsChangelogFile)
                ? TryGetLatestCommitDateAsync(repositoryApiRoot, rootFiles.First(IsChangelogFile), defaultBranch)
                : Task.FromResult<DateTimeOffset?>(null);

            await Task.WhenAll(releaseDataTask, readmeTask, issueDataTask, contributorDataTask, commitHealthTask,
                pullRequestQualityTask,
                medianPullRequestMergeDaysTask, workflowDataTask, branchProtectionTask, scorecardTask, changelogTask,
                securityPolicyTask, workflowFileSignalsTask, readmeUpdatedTask, changelogUpdatedTask);

            GitHubReleaseData releaseData = await releaseDataTask;
            GitHubReadmeData readmeData = await readmeTask;
            GitHubIssueData issueData = await issueDataTask;
            GitHubContributorData contributorData = await contributorDataTask;
            GitHubCommitHealthData commitHealthData = await commitHealthTask;
            GitHubPullRequestQualityData pullRequestQualityData = await pullRequestQualityTask;
            double? medianPullRequestMergeDays = await medianPullRequestMergeDaysTask;
            GitHubWorkflowData workflowData = await workflowDataTask;
            GitHubBranchProtectionData branchProtectionData = await branchProtectionTask;
            GitHubChangelogData changelogData = await changelogTask;
            GitHubSecurityPolicyData securityPolicyData = await securityPolicyTask;
            GitHubWorkflowFileSignals workflowFileSignals = await workflowFileSignalsTask;
            GitHubScorecardData scorecardData = await scorecardTask;
            DateTimeOffset? readmeUpdatedAt = await readmeUpdatedTask;
            DateTimeOffset? changelogUpdatedAt = await changelogUpdatedTask;

            return new GitHubRepositoryRiskData
            {
                CanonicalUrl = canonicalUrl,
                OwnerIsOrganization = ownerIsOrganization,
                OwnerCreatedAt = ownerCreatedAt,
                ContributorCount = contributorData.ContributorCount,
                TopContributorShare = contributorData.TopContributorShare,
                TopTwoContributorShare = contributorData.TopTwoContributorShare,
                RecentMaintainerCount = commitHealthData.RecentMaintainerCount,
                HasReadme = readmeData.Exists,
                HasDefaultReadme = readmeData.IsDefault,
                ReadmeUpdatedAt = readmeUpdatedAt,
                HasContributingGuide = rootFiles.Contains("CONTRIBUTING.md", StringComparer.OrdinalIgnoreCase),
                HasSecurityPolicy = securityPolicyData.Exists,
                HasDetailedSecurityPolicy = securityPolicyData.IsDetailed,
                HasCoordinatedDisclosure = securityPolicyData.HasCoordinatedDisclosure,
                HasChangelog = changelogData.Exists,
                HasDefaultChangelog = changelogData.IsDefault,
                ChangelogUpdatedAt = changelogUpdatedAt,
                OpenBugIssueCount = issueData.OpenBugIssueCount,
                StaleCriticalBugIssueCount = issueData.StaleCriticalBugIssueCount,
                MedianIssueResponseDays = issueData.MedianIssueResponseDays,
                MedianCriticalIssueResponseDays = issueData.MedianCriticalIssueResponseDays,
                IssueResponseCoverage = issueData.IssueResponseCoverage,
                MedianOpenBugAgeDays = issueData.MedianOpenBugAgeDays,
                ClosedBugIssueCountLast90Days = issueData.ClosedBugIssueCountLast90Days,
                ReopenedBugIssueCountLast90Days = issueData.ReopenedBugIssueCountLast90Days,
                IssueTriageWithinSevenDaysRate = issueData.TriageWithinSevenDaysRate,
                MedianPullRequestMergeDays = medianPullRequestMergeDays,
                ExternalContributionRate = pullRequestQualityData.ExternalContributionRate,
                UniqueReviewerCount = pullRequestQualityData.UniqueReviewerCount,
                ReviewerDiversityRatio = pullRequestQualityData.ReviewerDiversityRatio,
                RecentFailedWorkflowCount = workflowData.RecentFailedWorkflowCount,
                HasRecentSuccessfulWorkflowRun = workflowData.HasRecentSuccessfulWorkflowRun,
                WorkflowFailureRate = workflowData.FailureRate,
                HasFlakyWorkflowPattern = workflowData.HasFlakyPattern,
                RequiredStatusCheckCount = branchProtectionData.RequiredStatusCheckCount,
                WorkflowPlatformCount = workflowFileSignals.PlatformCount,
                HasCoverageWorkflowSignal = workflowFileSignals.HasCoverageSignal,
                HasReproducibleBuildSignal = workflowFileSignals.HasReproducibleBuildSignal || scorecardData.BinaryArtifactsScore >= 8.0,
                HasDependencyUpdateAutomation = workflowFileSignals.HasDependencyUpdateAutomation,
                HasTestSignal = workflowFileSignals.HasTestSignal,
                OpenSsfScore = scorecardData.Score,
                HasBranchProtection = branchProtectionData.IsProtected ?? scorecardData.HasBranchProtection,
                HasProvenanceAttestation = workflowFileSignals.HasProvenanceAttestation,
                HasVerifiedReleaseSignature = releaseData.HasVerifiedReleaseSignature,
                HasVerifiedPublisher = ownerIsOrganization,
                HasReleaseNotes = releaseData.HasReleaseNotes,
                HasSemVerReleaseTags = releaseData.HasSemVerReleaseTags,
                MeanReleaseIntervalDays = releaseData.MeanReleaseIntervalDays,
                MajorReleaseRatio = releaseData.MajorReleaseRatio,
                PrereleaseRatio = releaseData.PrereleaseRatio,
                RapidReleaseCorrectionCount = releaseData.RapidReleaseCorrectionCount,
                VerifiedCommitRatio = commitHealthData.VerifiedCommitRatio,
                MedianMaintainerActivityDays = commitHealthData.MedianMaintainerActivityDays,
                LastReleaseAt = releaseData.LastReleaseAt
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug("Failed to fetch GitHub repository risk metadata from {RepositoryApiRoot}: {Error}",
                repositoryApiRoot, ex.Message);
            return null;
        }
    }

    /// <summary>Sends an authenticated GET request and parses the JSON response.</summary>
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

    /// <summary>Lists filenames in the repository root directory.</summary>
    private async Task<string[]> GetRootFilesAsync(string repositoryApiRoot, string defaultBranch)
    {
        using JsonDocument contents = await GetJsonAsync($"{repositoryApiRoot}/contents?ref={Uri.EscapeDataString(defaultBranch)}");
        if (contents.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return contents.RootElement.EnumerateArray()
            .Select(item => TryReadString(item, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    /// <summary>Fetches the README and checks if it looks like a boilerplate.</summary>
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
        catch (HttpRequestException)
        {
            return new GitHubReadmeData { Exists = false };
        }
        catch (FormatException)
        {
            return new GitHubReadmeData { Exists = false };
        }
        catch (DecoderFallbackException)
        {
            return new GitHubReadmeData { Exists = false };
        }
    }

    /// <summary>Fetches contributor list and computes concentration metrics.</summary>
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

    /// <summary>Fetches open and closed bug issues and computes issue health metrics.</summary>
    private async Task<GitHubIssueData> GetIssueDataAsync(string repositoryApiRoot)
    {
        using JsonDocument issuesDoc = await GetJsonAsync($"{repositoryApiRoot}/issues?state=open&labels=bug&per_page=100");
        if (issuesDoc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new GitHubIssueData();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<double> responseDays = [];
        List<double> criticalResponseDays = [];
        List<double> openBugAgeDays = [];

        GitHubIssueSnapshot[] openIssues = issuesDoc.RootElement.EnumerateArray()
            .Where(issue => !issue.TryGetProperty("pull_request", out _))
            .Select(issue => new GitHubIssueSnapshot
            {
                Number = issue.TryGetProperty("number", out JsonElement numberElement) && numberElement.TryGetInt32(out int number)
                    ? number
                    : 0,
                CreatedAt = TryReadDate(issue, "created_at") ?? now,
                IsCritical = issue.TryGetProperty("labels", out JsonElement labels) &&
                             labels.ValueKind == JsonValueKind.Array &&
                             labels.EnumerateArray().Any(IsCriticalLabel),
                CommentsUrl = issue.TryGetProperty("comments_url", out JsonElement commentsElement)
                    ? commentsElement.GetString()
                    : null
            })
            .ToArray();

        int staleCriticalBugCount = openIssues.Count(issue => issue.IsCritical && issue.CreatedAt < now.AddMonths(-6));
        openBugAgeDays.AddRange(openIssues.Select(issue => (now - issue.CreatedAt).TotalDays));

        using var throttler = new SemaphoreSlim(8);
        Task<GitHubIssueResponseData>[] responseTasks = openIssues
            .Select(async issue =>
            {
                await throttler.WaitAsync();
                try
                {
                    return await TryGetIssueResponseDataAsync(issue);
                }
                finally
                {
                    throttler.Release();
                }
            })
            .ToArray();

        GitHubIssueResponseData[] responseResults = await Task.WhenAll(responseTasks);
        responseDays.AddRange(responseResults
            .Where(result => result.ResponseDays.HasValue)
            .Select(result => result.ResponseDays!.Value));
        criticalResponseDays.AddRange(openIssues.Zip(responseResults)
            .Where(pair => pair.First.IsCritical && pair.Second.ResponseDays.HasValue)
            .Select(pair => pair.Second.ResponseDays!.Value));

        int respondedIssueCount = responseResults.Count(result => result.HasMaintainerResponse);
        int triagedWithinSevenDaysCount = responseResults.Count(result => result.ResponseDays is <= 7);

        GitHubClosedIssueSnapshot[] closedIssues = await GetClosedBugIssuesAsync(repositoryApiRoot);
        int reopenedBugCount = await CountReopenedIssuesAsync(repositoryApiRoot, closedIssues.Take(20).ToArray());

        return new GitHubIssueData
        {
            OpenBugIssueCount = openIssues.Length,
            StaleCriticalBugIssueCount = staleCriticalBugCount,
            MedianIssueResponseDays = ComputeMedian(responseDays),
            MedianCriticalIssueResponseDays = ComputeMedian(criticalResponseDays),
            IssueResponseCoverage = openIssues.Length > 0 ? respondedIssueCount / (double)openIssues.Length : null,
            MedianOpenBugAgeDays = ComputeMedian(openBugAgeDays),
            ClosedBugIssueCountLast90Days = closedIssues.Length,
            ReopenedBugIssueCountLast90Days = reopenedBugCount,
            TriageWithinSevenDaysRate = openIssues.Length > 0 ? triagedWithinSevenDaysCount / (double)openIssues.Length : null
        };
    }

    /// <summary>Fetches the first maintainer comment on an issue to compute response time.</summary>
    private async Task<GitHubIssueResponseData> TryGetIssueResponseDataAsync(GitHubIssueSnapshot issue)
    {
        if (string.IsNullOrWhiteSpace(issue.CommentsUrl))
        {
            return new GitHubIssueResponseData();
        }

        try
        {
            using JsonDocument commentsDoc = await GetJsonAsync(issue.CommentsUrl);
            if (commentsDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new GitHubIssueResponseData();
            }

            DateTimeOffset? firstMaintainerComment = commentsDoc.RootElement.EnumerateArray()
                .Where(comment => comment.TryGetProperty("author_association", out JsonElement associationElement) &&
                                  IsMaintainerAssociation(associationElement.GetString()))
                .Select(comment => TryReadDate(comment, "created_at"))
                .Where(date => date.HasValue)
                .OrderBy(date => date)
                .FirstOrDefault();

            return firstMaintainerComment != null
                ? new GitHubIssueResponseData
                {
                    HasMaintainerResponse = true,
                    ResponseDays = (firstMaintainerComment.Value - issue.CreatedAt).TotalDays
                }
                : new GitHubIssueResponseData();
        }
        catch
        {
            return new GitHubIssueResponseData();
        }
    }

    /// <summary>Fetches merged PRs and computes median merge time in days.</summary>
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
                return createdAt.HasValue && mergedAt.HasValue ? (mergedAt.Value - createdAt.Value).TotalDays : (double?)null;
            })
            .Where(days => days.HasValue)
            .Select(days => days!.Value)
            .ToList();

        return ComputeMedian(mergeDays);
    }

    /// <summary>Fetches releases and computes semver/interval/correction metrics.</summary>
    private async Task<GitHubReleaseData> GetReleaseDataAsync(string repositoryApiRoot)
    {
        try
        {
            using JsonDocument releaseDoc = await GetJsonAsync($"{repositoryApiRoot}/releases?per_page=10");
            if (releaseDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new GitHubReleaseData();
            }

            JsonElement[] releases = releaseDoc.RootElement.EnumerateArray().ToArray();
            int semVerReleaseCount = 0;
            bool hasReleaseNotes = releases.Any(release =>
                release.TryGetProperty("body", out JsonElement bodyElement) &&
                !string.IsNullOrWhiteSpace(bodyElement.GetString()) &&
                bodyElement.GetString()!.Length >= 80);

            DateTimeOffset? lastReleaseAt = releases
                .Select(release => TryReadDate(release, "published_at") ?? TryReadDate(release, "created_at"))
                .Where(date => date.HasValue)
                .OrderByDescending(date => date)
                .FirstOrDefault();

            int prereleaseCount = releases.Count(release =>
                release.TryGetProperty("prerelease", out JsonElement prereleaseElement) &&
                prereleaseElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                prereleaseElement.GetBoolean());

            List<DateTimeOffset> publishedDates = releases
                .Select(release => TryReadDate(release, "published_at") ?? TryReadDate(release, "created_at"))
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .OrderBy(date => date)
                .ToList();
            List<(DateTimeOffset PublishedAt, NuGet.Versioning.NuGetVersion Version)> parsedReleaseVersions = [];

            foreach (JsonElement release in releases)
            {
                string? tagName = release.TryGetProperty("tag_name", out JsonElement tagNameElement)
                    ? tagNameElement.GetString()
                    : null;

                if (!TryParseReleaseVersion(tagName, out NuGet.Versioning.NuGetVersion? parsedVersion) || parsedVersion is null)
                {
                    continue;
                }

                semVerReleaseCount++;
                DateTimeOffset? publishedAt = TryReadDate(release, "published_at") ?? TryReadDate(release, "created_at");
                if (publishedAt != null)
                {
                    parsedReleaseVersions.Add((publishedAt.Value, parsedVersion));
                }
            }

            int rapidCorrections = 0;
            for (int i = 1; i < publishedDates.Count; i++)
            {
                if ((publishedDates[i] - publishedDates[i - 1]).TotalDays <= 3)
                {
                    rapidCorrections++;
                }
            }

            bool? hasVerifiedReleaseSignature = releases
                .Select(TryReadVerifiedReleaseSignature)
                .FirstOrDefault();

            return new GitHubReleaseData
            {
                LastReleaseAt = lastReleaseAt,
                HasReleaseNotes = hasReleaseNotes,
                HasSemVerReleaseTags = releases.Length > 0 ? semVerReleaseCount == releases.Length : null,
                MeanReleaseIntervalDays = ComputeAverageIntervalDays(publishedDates),
                MajorReleaseRatio = ComputeMajorReleaseRatio(parsedReleaseVersions),
                PrereleaseRatio = releases.Length > 0 ? prereleaseCount / (double)releases.Length : null,
                RapidReleaseCorrectionCount = rapidCorrections,
                HasVerifiedReleaseSignature = hasVerifiedReleaseSignature
            };
        }
        catch (HttpRequestException)
        {
            return new GitHubReleaseData();
        }
        catch (JsonException)
        {
            return new GitHubReleaseData();
        }
    }

    /// <summary>Fetches merged PRs and computes external contribution and reviewer diversity metrics.</summary>
    private async Task<GitHubPullRequestQualityData> GetPullRequestQualityDataAsync(string repositoryApiRoot)
    {
        try
        {
            using JsonDocument pullsDoc = await GetJsonAsync($"{repositoryApiRoot}/pulls?state=closed&sort=updated&direction=desc&per_page=30");
            if (pullsDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new GitHubPullRequestQualityData();
            }

            GitHubPullRequestSnapshot[] mergedPulls = pullsDoc.RootElement.EnumerateArray()
                .Where(pr => pr.TryGetProperty("merged_at", out JsonElement mergedAtElement) &&
                             mergedAtElement.ValueKind == JsonValueKind.String)
                .Select(pr => new GitHubPullRequestSnapshot
                {
                    Number = pr.TryGetProperty("number", out JsonElement numberElement) && numberElement.TryGetInt32(out int number) ? number : 0,
                    AuthorAssociation = pr.TryGetProperty("author_association", out JsonElement associationElement) ? associationElement.GetString() : null
                })
                .Where(pr => pr.Number > 0)
                .ToArray();

            if (mergedPulls.Length == 0)
            {
                return new GitHubPullRequestQualityData();
            }

            int externalContributionCount = mergedPulls.Count(pr => IsExternalAuthorAssociation(pr.AuthorAssociation));
            HashSet<string> uniqueReviewers = [];

            using var throttler = new SemaphoreSlim(6);
            Task<string[]>[] reviewTasks = mergedPulls.Take(20).Select(async pr =>
            {
                await throttler.WaitAsync();
                try
                {
                    return await GetReviewerLoginsAsync(repositoryApiRoot, pr.Number);
                }
                finally
                {
                    throttler.Release();
                }
            }).ToArray();

            string[][] reviewResults = await Task.WhenAll(reviewTasks);
            foreach (string reviewer in reviewResults.SelectMany(result => result))
            {
                uniqueReviewers.Add(reviewer);
            }

            return new GitHubPullRequestQualityData
            {
                ExternalContributionRate = externalContributionCount / (double)mergedPulls.Length,
                UniqueReviewerCount = uniqueReviewers.Count,
                ReviewerDiversityRatio = mergedPulls.Length > 0 ? uniqueReviewers.Count / (double)mergedPulls.Length : null
            };
        }
        catch
        {
            return new GitHubPullRequestQualityData();
        }
    }

    /// <summary>Fetches recent workflow runs and computes failure/flaky metrics.</summary>
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
            int completedRuns = workflowRuns.Count(run =>
                string.Equals(run.TryGetProperty("status", out JsonElement status) ? status.GetString() : null,
                    "completed", StringComparison.OrdinalIgnoreCase));
            int failedRuns = workflowRuns.Count(run =>
                string.Equals(run.TryGetProperty("conclusion", out JsonElement conclusion) ? conclusion.GetString() : null,
                    "failure", StringComparison.OrdinalIgnoreCase));
            bool hasSuccess = workflowRuns.Any(run =>
                string.Equals(run.TryGetProperty("conclusion", out JsonElement conclusion) ? conclusion.GetString() : null,
                    "success", StringComparison.OrdinalIgnoreCase));

            return new GitHubWorkflowData
            {
                RecentFailedWorkflowCount = failedRuns,
                HasRecentSuccessfulWorkflowRun = hasSuccess,
                FailureRate = completedRuns > 0 ? failedRuns / (double)completedRuns : null,
                HasFlakyPattern = completedRuns >= 4 && failedRuns > 0 && hasSuccess && failedRuns < completedRuns
            };
        }
        catch
        {
            return new GitHubWorkflowData();
        }
    }

    /// <summary>Fetches branch protection settings for the default branch.</summary>
    private async Task<GitHubBranchProtectionData> GetBranchProtectionDataAsync(string repositoryApiRoot, string defaultBranch)
    {
        try
        {
            using JsonDocument branchDoc = await GetJsonAsync(
                $"{repositoryApiRoot}/branches/{Uri.EscapeDataString(defaultBranch)}");

            bool? isProtected = branchDoc.RootElement.TryGetProperty("protected", out JsonElement protectedElement) &&
                                protectedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? protectedElement.GetBoolean()
                : null;

            int? requiredStatusCheckCount = null;
            if (branchDoc.RootElement.TryGetProperty("protection", out JsonElement protectionElement) &&
                protectionElement.TryGetProperty("required_status_checks", out JsonElement checksElement) &&
                checksElement.TryGetProperty("contexts", out JsonElement contextsElement) &&
                contextsElement.ValueKind == JsonValueKind.Array)
            {
                requiredStatusCheckCount = contextsElement.GetArrayLength();
            }

            return new GitHubBranchProtectionData
            {
                IsProtected = isProtected,
                RequiredStatusCheckCount = requiredStatusCheckCount
            };
        }
        catch
        {
            return new GitHubBranchProtectionData();
        }
    }

    /// <summary>Reads CHANGELOG file and checks if it looks like a boilerplate.</summary>
    private async Task<GitHubChangelogData> GetChangelogDataAsync(string repositoryApiRoot, string defaultBranch, string[] rootFiles)
    {
        string? changelogFile = rootFiles.FirstOrDefault(IsChangelogFile);

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

    /// <summary>Reads SECURITY.md and checks detail/disclosure quality.</summary>
    private async Task<GitHubSecurityPolicyData> GetSecurityPolicyDataAsync(string repositoryApiRoot, string defaultBranch, string[] rootFiles)
    {
        string? securityPolicyPath = rootFiles.FirstOrDefault(file =>
            file.Equals("SECURITY.md", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("SECURITY", StringComparison.OrdinalIgnoreCase));

        securityPolicyPath ??= ".github/SECURITY.md";

        string content = await TryGetFileContentAsync(repositoryApiRoot, securityPolicyPath, defaultBranch);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new GitHubSecurityPolicyData();
        }

        string normalized = content.ToLowerInvariant();
        bool hasContact = normalized.Contains("security@") ||
                          normalized.Contains("contact") ||
                          normalized.Contains("report");
        bool hasPrivateChannel = normalized.Contains("private") ||
                                 normalized.Contains("gpg") ||
                                 normalized.Contains("pgp") ||
                                 normalized.Contains("encrypted");

        return new GitHubSecurityPolicyData
        {
            Exists = true,
            IsDetailed = content.Length >= 400 && hasContact,
            HasCoordinatedDisclosure = hasContact && hasPrivateChannel
        };
    }

    /// <summary>Analyzes recent commits for maintainer activity and commit verification ratio.</summary>
    private async Task<GitHubCommitHealthData> GetCommitHealthDataAsync(string repositoryApiRoot, string defaultBranch)
    {
        try
        {
            using JsonDocument commitsDoc = await GetJsonAsync(
                $"{repositoryApiRoot}/commits?sha={Uri.EscapeDataString(defaultBranch)}&since={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMonths(-12).ToString("O"))}&per_page=100");

            if (commitsDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new GitHubCommitHealthData();
            }

            Dictionary<string, DateTimeOffset> lastActivityByMaintainer = [];
            int verifiedCommitCount = 0;
            int signedCommitSampleCount = 0;

            foreach (JsonElement commit in commitsDoc.RootElement.EnumerateArray())
            {
                string? identity =
                    commit.TryGetProperty("author", out JsonElement authorElement) &&
                    authorElement.TryGetProperty("login", out JsonElement loginElement)
                        ? loginElement.GetString()
                        : commit.TryGetProperty("commit", out JsonElement commitElement) &&
                          commitElement.TryGetProperty("author", out JsonElement nestedAuthor) &&
                          nestedAuthor.TryGetProperty("email", out JsonElement emailElement)
                            ? emailElement.GetString()
                            : null;

                if (string.IsNullOrWhiteSpace(identity) || IsBotIdentity(identity))
                {
                    continue;
                }

                DateTimeOffset? committedAt = commit.TryGetProperty("commit", out JsonElement commitData) &&
                                              commitData.TryGetProperty("author", out JsonElement commitAuthor)
                    ? TryReadDate(commitAuthor, "date")
                    : null;

                if (committedAt != null)
                {
                    DateTimeOffset activityAt = committedAt.Value;
                    if (!lastActivityByMaintainer.TryGetValue(identity, out DateTimeOffset existing) || activityAt > existing)
                    {
                        lastActivityByMaintainer[identity] = activityAt;
                    }
                }

                bool? verified = TryReadCommitVerification(commit);
                if (verified != null)
                {
                    signedCommitSampleCount++;
                    if (verified.Value)
                    {
                        verifiedCommitCount++;
                    }
                }
            }

            List<double> maintainerActivityDays = lastActivityByMaintainer.Values
                .Select(date => (DateTimeOffset.UtcNow - date).TotalDays)
                .ToList();

            int recentMaintainerCount = lastActivityByMaintainer.Values.Count(date => date >= DateTimeOffset.UtcNow.AddMonths(-6));
            return new GitHubCommitHealthData
            {
                RecentMaintainerCount = recentMaintainerCount,
                MedianMaintainerActivityDays = ComputeMedian(maintainerActivityDays),
                VerifiedCommitRatio = signedCommitSampleCount > 0 ? verifiedCommitCount / (double)signedCommitSampleCount : null
            };
        }
        catch
        {
            return new GitHubCommitHealthData();
        }
    }

    /// <summary>Fetches bug issues closed in the last 90 days.</summary>
    private async Task<GitHubClosedIssueSnapshot[]> GetClosedBugIssuesAsync(string repositoryApiRoot)
    {
        try
        {
            string since = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-90).ToString("O"));
            using JsonDocument issuesDoc = await GetJsonAsync(
                $"{repositoryApiRoot}/issues?state=closed&labels=bug&sort=updated&direction=desc&since={since}&per_page=50");

            if (issuesDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return issuesDoc.RootElement.EnumerateArray()
                .Where(issue => !issue.TryGetProperty("pull_request", out _))
                .Select(issue => new GitHubClosedIssueSnapshot
                {
                    Number = issue.TryGetProperty("number", out JsonElement numberElement) && numberElement.TryGetInt32(out int number)
                        ? number
                        : 0,
                    ClosedAt = TryReadDate(issue, "closed_at")
                })
                .Where(issue => issue is { Number: > 0, ClosedAt: not null } &&
                                issue.ClosedAt >= DateTimeOffset.UtcNow.AddDays(-90))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Counts how many of the given closed issues were subsequently reopened.</summary>
    private async Task<int> CountReopenedIssuesAsync(string repositoryApiRoot, GitHubClosedIssueSnapshot[] closedIssues)
    {
        if (closedIssues.Length == 0)
        {
            return 0;
        }

        using var throttler = new SemaphoreSlim(6);
        Task<bool>[] tasks = closedIssues.Select(async issue =>
        {
            await throttler.WaitAsync();
            try
            {
                using JsonDocument eventsDoc = await GetJsonAsync(
                    $"{repositoryApiRoot}/issues/{issue.Number}/timeline?per_page=100");

                return eventsDoc.RootElement.ValueKind == JsonValueKind.Array &&
                       eventsDoc.RootElement.EnumerateArray().Any(eventItem =>
                           string.Equals(eventItem.TryGetProperty("event", out JsonElement eventElement) ? eventElement.GetString() : null,
                               "reopened", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        bool[] reopened = await Task.WhenAll(tasks);
        return reopened.Count(value => value);
    }

    /// <summary>Returns the date of the most recent commit that touched the given file path.</summary>
    private async Task<DateTimeOffset?> TryGetLatestCommitDateAsync(string repositoryApiRoot, string path, string defaultBranch)
    {
        try
        {
            using JsonDocument commitsDoc = await GetJsonAsync(
                $"{repositoryApiRoot}/commits?sha={Uri.EscapeDataString(defaultBranch)}&path={Uri.EscapeDataString(path)}&per_page=1");

            if (commitsDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement commit = commitsDoc.RootElement.EnumerateArray().FirstOrDefault();
            return commit.ValueKind == JsonValueKind.Undefined
                ? null
                : TryReadDate(commit.GetProperty("commit").GetProperty("author"), "date");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads workflow YAML files and detects signals for testing, coverage, reproducibility, and dependency automation.</summary>
    private async Task<GitHubWorkflowFileSignals> GetWorkflowFileSignalsAsync(string repositoryApiRoot, string defaultBranch, string[] rootFiles)
    {
        try
        {
            using JsonDocument workflowsDoc = await GetJsonAsync(
                $"{repositoryApiRoot}/contents/.github/workflows?ref={Uri.EscapeDataString(defaultBranch)}");

            if (workflowsDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new GitHubWorkflowFileSignals();
            }

            string[] paths = workflowsDoc.RootElement.EnumerateArray()
                .Select(item => TryReadString(item, "path"))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToArray();

            string[] contents = await Task.WhenAll(paths.Select(path => TryGetFileContentAsync(repositoryApiRoot, path, defaultBranch)));
            string combined = string.Join("\n", contents).ToLowerInvariant();
            string dependabotConfig = await TryGetFileContentAsync(repositoryApiRoot, ".github/dependabot.yml", defaultBranch);

            HashSet<string> platforms = [];
            if (combined.Contains("ubuntu", StringComparison.OrdinalIgnoreCase))
            {
                platforms.Add("ubuntu");
            }

            if (combined.Contains("windows", StringComparison.OrdinalIgnoreCase))
            {
                platforms.Add("windows");
            }

            if (combined.Contains("macos", StringComparison.OrdinalIgnoreCase))
            {
                platforms.Add("macos");
            }

            if (combined.Contains("self-hosted", StringComparison.OrdinalIgnoreCase))
            {
                platforms.Add("self-hosted");
            }

            return new GitHubWorkflowFileSignals
            {
                HasProvenanceAttestation = combined.Contains("slsa", StringComparison.OrdinalIgnoreCase) ||
                                           combined.Contains("provenance", StringComparison.OrdinalIgnoreCase) ||
                                           combined.Contains("attest", StringComparison.OrdinalIgnoreCase),
                HasCoverageSignal = combined.Contains("coverage", StringComparison.OrdinalIgnoreCase) ||
                                    combined.Contains("codecov", StringComparison.OrdinalIgnoreCase) ||
                                    combined.Contains("coveralls", StringComparison.OrdinalIgnoreCase) ||
                                    combined.Contains("coverlet", StringComparison.OrdinalIgnoreCase),
                HasReproducibleBuildSignal = combined.Contains("deterministic", StringComparison.OrdinalIgnoreCase) ||
                                             combined.Contains("reproducible", StringComparison.OrdinalIgnoreCase) ||
                                             combined.Contains("source-build", StringComparison.OrdinalIgnoreCase),
                HasDependencyUpdateAutomation = !string.IsNullOrWhiteSpace(dependabotConfig) ||
                                               combined.Contains("dependabot", StringComparison.OrdinalIgnoreCase) ||
                                               combined.Contains("renovate", StringComparison.OrdinalIgnoreCase) ||
                                               rootFiles.Contains("renovate.json", StringComparer.OrdinalIgnoreCase) ||
                                               rootFiles.Contains("renovate.json5", StringComparer.OrdinalIgnoreCase),
                HasTestSignal = combined.Contains("dotnet test", StringComparison.OrdinalIgnoreCase) ||
                                combined.Contains("npm test", StringComparison.OrdinalIgnoreCase) ||
                                combined.Contains("pnpm test", StringComparison.OrdinalIgnoreCase) ||
                                combined.Contains("yarn test", StringComparison.OrdinalIgnoreCase) ||
                                combined.Contains("pytest", StringComparison.OrdinalIgnoreCase) ||
                                combined.Contains("junit", StringComparison.OrdinalIgnoreCase) ||
                                rootFiles.Contains("test", StringComparer.OrdinalIgnoreCase) ||
                                rootFiles.Contains("tests", StringComparer.OrdinalIgnoreCase) ||
                                rootFiles.Contains("spec", StringComparer.OrdinalIgnoreCase) ||
                                rootFiles.Contains("specs", StringComparer.OrdinalIgnoreCase),
                PlatformCount = platforms.Count
            };
        }
        catch
        {
            return new GitHubWorkflowFileSignals();
        }
    }

    /// <summary>Returns unique non-bot reviewer logins for a pull request.</summary>
    private async Task<string[]> GetReviewerLoginsAsync(string repositoryApiRoot, int pullRequestNumber)
    {
        try
        {
            using JsonDocument reviewsDoc = await GetJsonAsync($"{repositoryApiRoot}/pulls/{pullRequestNumber}/reviews?per_page=100");
            if (reviewsDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return reviewsDoc.RootElement.EnumerateArray()
                .Where(review => review.TryGetProperty("user", out JsonElement userElement) &&
                                 userElement.TryGetProperty("login", out JsonElement loginElement) &&
                                 !string.IsNullOrWhiteSpace(loginElement.GetString()))
                .Select(review => review.GetProperty("user").GetProperty("login").GetString()!)
                .Where(login => !IsBotIdentity(login))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Fetches OpenSSF Scorecard data from the security scorecard API.</summary>
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
            double? binaryArtifactsScore = null;
            if (root.TryGetProperty("checks", out JsonElement checks) && checks.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement check in checks.EnumerateArray())
                {
                    string? name = check.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : null;
                    if (string.Equals(name, "Branch-Protection", StringComparison.OrdinalIgnoreCase) &&
                        check.TryGetProperty("score", out JsonElement branchScoreElement) &&
                        branchScoreElement.TryGetDouble(out double branchScore))
                    {
                        hasBranchProtection = branchScore > 0;
                    }

                    if (string.Equals(name, "Binary-Artifacts", StringComparison.OrdinalIgnoreCase) &&
                        check.TryGetProperty("score", out JsonElement binaryScoreElement) &&
                        binaryScoreElement.TryGetDouble(out double binaryScore))
                    {
                        binaryArtifactsScore = binaryScore;
                    }
                }
            }

            return new GitHubScorecardData
            {
                Score = root.TryGetProperty("score", out JsonElement score) && score.TryGetDouble(out double value) ? value : null,
                HasBranchProtection = hasBranchProtection,
                BinaryArtifactsScore = binaryArtifactsScore
            };
        }
        catch
        {
            return new GitHubScorecardData();
        }
    }

    /// <summary>Fetches and decodes the base64-encoded content of a file via the GitHub contents API.</summary>
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
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException)
        {
        }

        return string.Empty;
    }

    /// <summary>Converts a GitHub HTML URL to the API repository root URL.</summary>
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

    /// <summary>Returns true if the content appears to be a boilerplate README.</summary>
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

    /// <summary>Returns true if the content appears to be a boilerplate changelog.</summary>
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

    /// <summary>Returns true if the filename is a known changelog filename.</summary>
    private static bool IsChangelogFile(string file) =>
        file.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
        file.Equals("CHANGELOG", StringComparison.OrdinalIgnoreCase) ||
        file.Equals("RELEASE_NOTES.md", StringComparison.OrdinalIgnoreCase) ||
        file.Equals("NEWS.md", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true if the issue label name indicates critical severity or a security issue.</summary>
    private static bool IsCriticalLabel(JsonElement label)
    {
        string name = label.TryGetProperty("name", out JsonElement nameElement)
            ? nameElement.GetString() ?? string.Empty
            : string.Empty;
        return name.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("security", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("sev1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tries to read whether a GitHub release has a verified signature.</summary>
    private static bool? TryReadVerifiedReleaseSignature(JsonElement release)
    {
        if (!release.TryGetProperty("target_commitish", out _))
        {
            return null;
        }

        if (release.TryGetProperty("immutable", out JsonElement immutableElement) &&
            immutableElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return immutableElement.GetBoolean();
        }

        return null;
    }

    /// <summary>Strips v/release prefixes and tries to parse the tag as a NuGetVersion.</summary>
    private static bool TryParseReleaseVersion(string? tagName, out NuGet.Versioning.NuGetVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        string normalized = tagName.Trim();
        if (normalized.StartsWith("release-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["release-".Length..];
        }

        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        return NuGet.Versioning.NuGetVersion.TryParse(normalized, out version);
    }

    /// <summary>Computes the fraction of consecutive release transitions that were major-version bumps.</summary>
    internal static double? ComputeMajorReleaseRatio(
        IReadOnlyList<(DateTimeOffset PublishedAt, NuGet.Versioning.NuGetVersion Version)> releases)
    {
        if (releases.Count < 2)
        {
            return null;
        }

        var orderedReleases = releases
            .OrderBy(release => release.PublishedAt)
            .ToArray();

        int transitionCount = orderedReleases.Length - 1;
        int majorTransitionCount = 0;

        for (int i = 1; i < orderedReleases.Length; i++)
        {
            if (orderedReleases[i].Version.Major > orderedReleases[i - 1].Version.Major)
            {
                majorTransitionCount++;
            }
        }

        return transitionCount > 0 ? majorTransitionCount / (double)transitionCount : null;
    }

    /// <summary>Computes the mean interval in days between consecutive dates.</summary>
    private static double? ComputeAverageIntervalDays(IReadOnlyList<DateTimeOffset> dates)
    {
        if (dates.Count < 2)
        {
            return null;
        }

        double total = 0;
        for (int i = 1; i < dates.Count; i++)
        {
            total += (dates[i] - dates[i - 1]).TotalDays;
        }

        return total / (dates.Count - 1);
    }

    /// <summary>Returns true if the login/email looks like a bot (e.g. [bot] suffix, dependabot).</summary>
    private static bool IsBotIdentity(string? identity) =>
        string.IsNullOrWhiteSpace(identity) ||
        identity.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) ||
        identity.Contains("dependabot", StringComparison.OrdinalIgnoreCase) ||
        identity.Contains("copilot", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true if the author association indicates an external (non-member) contributor.</summary>
    private static bool IsExternalAuthorAssociation(string? association) =>
        association is "NONE" or "FIRST_TIME_CONTRIBUTOR" or "FIRST_TIMER" or "CONTRIBUTOR";

    /// <summary>Reads the commit.verification.verified boolean from a commit JSON element.</summary>
    private static bool? TryReadCommitVerification(JsonElement commit)
    {
        if (!commit.TryGetProperty("commit", out JsonElement commitElement) ||
            !commitElement.TryGetProperty("verification", out JsonElement verificationElement) ||
            !verificationElement.TryGetProperty("verified", out JsonElement verifiedElement) ||
            verifiedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        return verifiedElement.GetBoolean();
    }

    /// <summary>Returns true if the declared and canonical GitHub repo identifiers differ.</summary>
    private static bool HasRepositoryOwnershipOrRenameChurn(string? declaredRepositoryUrl, string canonicalUrl)
    {
        string? declaredIdentifier = TryGetGitHubIdentifier(declaredRepositoryUrl);
        string? canonicalIdentifier = TryGetGitHubIdentifier(canonicalUrl);
        return !string.IsNullOrWhiteSpace(canonicalIdentifier) &&
               !string.IsNullOrWhiteSpace(declaredIdentifier) &&
               !canonicalIdentifier.Equals(declaredIdentifier, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Extracts the "owner/repo" identifier from a GitHub URL.</summary>
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

    /// <summary>Reads and parses a date property from a JSON element.</summary>
    private static DateTimeOffset? TryReadDate(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property) &&
            DateTimeOffset.TryParse(property.GetString(), out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>Reads a string property from a JSON element.</summary>
    private static string? TryReadString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    /// <summary>Returns true if the author association is OWNER, MEMBER, or COLLABORATOR.</summary>
    private static bool IsMaintainerAssociation(string? association)
    {
        return association is "OWNER" or "MEMBER" or "COLLABORATOR";
    }

    /// <summary>Computes the median of a list, returning null if empty.</summary>
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

    /// <summary>Aggregated repository risk metrics for a GitHub repository.</summary>
    private sealed class GitHubRepositoryRiskData
    {
        /// <summary>The canonical HTML URL of the repository as returned by the GitHub API.</summary>
        public string CanonicalUrl { get; init; } = string.Empty;

        /// <summary>Indicates whether the repository owner is an organization.</summary>
        public bool OwnerIsOrganization { get; init; }

        /// <summary>The date the repository owner account was created.</summary>
        public DateTimeOffset? OwnerCreatedAt { get; init; }

        /// <summary>The total number of non-bot contributors.</summary>
        public int ContributorCount { get; init; }

        /// <summary>The fraction of total contributions made by the top contributor.</summary>
        public double? TopContributorShare { get; init; }

        /// <summary>The fraction of total contributions made by the top two contributors combined.</summary>
        public double? TopTwoContributorShare { get; init; }

        /// <summary>The number of maintainers with commit activity in the last six months.</summary>
        public int? RecentMaintainerCount { get; init; }

        /// <summary>Indicates whether the repository has a README file.</summary>
        public bool HasReadme { get; init; }

        /// <summary>Indicates whether the README appears to be a boilerplate default.</summary>
        public bool HasDefaultReadme { get; init; }

        /// <summary>The date of the most recent commit that touched the README file.</summary>
        public DateTimeOffset? ReadmeUpdatedAt { get; init; }

        /// <summary>Indicates whether the repository has a CONTRIBUTING.md file.</summary>
        public bool HasContributingGuide { get; init; }

        /// <summary>Indicates whether the repository has a SECURITY.md file.</summary>
        public bool HasSecurityPolicy { get; init; }

        /// <summary>Indicates whether the security policy contains detailed contact and reporting information.</summary>
        public bool? HasDetailedSecurityPolicy { get; init; }

        /// <summary>Indicates whether the security policy describes a coordinated disclosure process.</summary>
        public bool? HasCoordinatedDisclosure { get; init; }

        /// <summary>Indicates whether the repository has a CHANGELOG file.</summary>
        public bool HasChangelog { get; init; }

        /// <summary>Indicates whether the CHANGELOG appears to be a boilerplate default.</summary>
        public bool HasDefaultChangelog { get; init; }

        /// <summary>The date of the most recent commit that touched the CHANGELOG file.</summary>
        public DateTimeOffset? ChangelogUpdatedAt { get; init; }

        /// <summary>The number of currently open bug issues.</summary>
        public int OpenBugIssueCount { get; init; }

        /// <summary>The number of critical bug issues that have been open for more than six months.</summary>
        public int StaleCriticalBugIssueCount { get; init; }

        /// <summary>The median number of days until a maintainer first responds to an open bug issue.</summary>
        public double? MedianIssueResponseDays { get; init; }

        /// <summary>The median number of days until a maintainer first responds to a critical open bug issue.</summary>
        public double? MedianCriticalIssueResponseDays { get; init; }

        /// <summary>The fraction of open bug issues that have received at least one maintainer response.</summary>
        public double? IssueResponseCoverage { get; init; }

        /// <summary>The median age in days of currently open bug issues.</summary>
        public double? MedianOpenBugAgeDays { get; init; }

        /// <summary>The number of bug issues closed in the last 90 days.</summary>
        public int? ClosedBugIssueCountLast90Days { get; init; }

        /// <summary>The number of bug issues that were reopened after being closed in the last 90 days.</summary>
        public int? ReopenedBugIssueCountLast90Days { get; init; }

        /// <summary>The fraction of open bug issues that received a maintainer response within seven days.</summary>
        public double? IssueTriageWithinSevenDaysRate { get; init; }

        /// <summary>The median number of days from pull request creation to merge.</summary>
        public double? MedianPullRequestMergeDays { get; init; }

        /// <summary>The fraction of recently merged pull requests authored by external contributors.</summary>
        public double? ExternalContributionRate { get; init; }

        /// <summary>The number of unique reviewers across recently merged pull requests.</summary>
        public int? UniqueReviewerCount { get; init; }

        /// <summary>The ratio of unique reviewers to total recently merged pull requests.</summary>
        public double? ReviewerDiversityRatio { get; init; }

        /// <summary>The number of failed workflow runs among the most recent runs on the default branch.</summary>
        public int? RecentFailedWorkflowCount { get; init; }

        /// <summary>Indicates whether there is at least one recent successful workflow run on the default branch.</summary>
        public bool? HasRecentSuccessfulWorkflowRun { get; init; }

        /// <summary>The fraction of completed workflow runs that failed.</summary>
        public double? WorkflowFailureRate { get; init; }

        /// <summary>Indicates whether the workflow runs show a flaky pattern (intermittent failures and successes).</summary>
        public bool? HasFlakyWorkflowPattern { get; init; }

        /// <summary>The number of required status checks configured on the default branch.</summary>
        public int? RequiredStatusCheckCount { get; init; }

        /// <summary>The number of distinct operating system platforms targeted by workflow files.</summary>
        public int? WorkflowPlatformCount { get; init; }

        /// <summary>Indicates whether workflow files contain a coverage reporting signal.</summary>
        public bool? HasCoverageWorkflowSignal { get; init; }

        /// <summary>Indicates whether workflow files or Scorecard results suggest reproducible builds.</summary>
        public bool? HasReproducibleBuildSignal { get; init; }

        /// <summary>Indicates whether the repository uses automated dependency update tooling.</summary>
        public bool? HasDependencyUpdateAutomation { get; init; }

        /// <summary>Indicates whether workflow files contain a test execution signal.</summary>
        public bool? HasTestSignal { get; init; }

        /// <summary>The OpenSSF Scorecard aggregate score for the repository.</summary>
        public double? OpenSsfScore { get; init; }

        /// <summary>Indicates whether the default branch has branch protection enabled.</summary>
        public bool? HasBranchProtection { get; init; }

        /// <summary>Indicates whether workflow files contain a provenance attestation signal.</summary>
        public bool? HasProvenanceAttestation { get; init; }

        /// <summary>Indicates whether the most recent release has a verified signature.</summary>
        public bool? HasVerifiedReleaseSignature { get; init; }

        /// <summary>Indicates whether the repository owner is an organization, used as a proxy for a verified publisher.</summary>
        public bool? HasVerifiedPublisher { get; init; }

        /// <summary>Indicates whether releases include substantive release notes.</summary>
        public bool? HasReleaseNotes { get; init; }

        /// <summary>Indicates whether all releases use SemVer-compatible tags.</summary>
        public bool? HasSemVerReleaseTags { get; init; }

        /// <summary>The mean number of days between consecutive releases.</summary>
        public double? MeanReleaseIntervalDays { get; init; }

        /// <summary>The fraction of consecutive release transitions that were major-version bumps.</summary>
        public double? MajorReleaseRatio { get; init; }

        /// <summary>The fraction of releases marked as pre-releases.</summary>
        public double? PrereleaseRatio { get; init; }

        /// <summary>The number of release pairs published within three days of each other, indicating rapid corrections.</summary>
        public int? RapidReleaseCorrectionCount { get; init; }

        /// <summary>The fraction of sampled commits that have a verified signature.</summary>
        public double? VerifiedCommitRatio { get; init; }

        /// <summary>The median number of days since the last commit activity for each active maintainer.</summary>
        public double? MedianMaintainerActivityDays { get; init; }

        /// <summary>The publication date of the most recent release.</summary>
        public DateTimeOffset? LastReleaseAt { get; init; }
    }

    /// <summary>Lightweight snapshot of an open GitHub issue for processing.</summary>
    private sealed class GitHubIssueSnapshot
    {
        /// <summary>The GitHub issue number.</summary>
        [UsedImplicitly]
        public int Number { get; init; }

        /// <summary>The date and time the issue was created.</summary>
        public DateTimeOffset CreatedAt { get; init; }

        /// <summary>Indicates whether the issue has a label indicating critical severity or a security concern.</summary>
        public bool IsCritical { get; init; }

        /// <summary>The URL to fetch comments for this issue.</summary>
        public string? CommentsUrl { get; init; }
    }

    /// <summary>Lightweight snapshot of a closed GitHub issue.</summary>
    private sealed class GitHubClosedIssueSnapshot
    {
        /// <summary>The GitHub issue number.</summary>
        [UsedImplicitly]
        public int Number { get; init; }

        /// <summary>The date and time the issue was closed.</summary>
        public DateTimeOffset? ClosedAt { get; init; }
    }

    /// <summary>Aggregated bug issue health metrics.</summary>
    private sealed class GitHubIssueData
    {
        /// <summary>The number of currently open bug issues.</summary>
        public int OpenBugIssueCount { get; init; }

        /// <summary>The number of critical bug issues open for more than six months.</summary>
        public int StaleCriticalBugIssueCount { get; init; }

        /// <summary>The median number of days until a maintainer first responds to an open bug issue.</summary>
        public double? MedianIssueResponseDays { get; init; }

        /// <summary>The median number of days until a maintainer first responds to a critical open bug issue.</summary>
        public double? MedianCriticalIssueResponseDays { get; init; }

        /// <summary>The fraction of open bug issues that have received at least one maintainer response.</summary>
        public double? IssueResponseCoverage { get; init; }

        /// <summary>The median age in days of currently open bug issues.</summary>
        public double? MedianOpenBugAgeDays { get; init; }

        /// <summary>The number of bug issues closed in the last 90 days.</summary>
        public int? ClosedBugIssueCountLast90Days { get; init; }

        /// <summary>The number of bug issues reopened after being closed in the last 90 days.</summary>
        public int? ReopenedBugIssueCountLast90Days { get; init; }

        /// <summary>The fraction of open bug issues that received a maintainer response within seven days.</summary>
        public double? TriageWithinSevenDaysRate { get; init; }
    }

    /// <summary>Result of fetching maintainer response data for an issue.</summary>
    private sealed class GitHubIssueResponseData
    {
        /// <summary>Indicates whether a maintainer has responded to the issue.</summary>
        public bool HasMaintainerResponse { get; init; }

        /// <summary>The number of days between issue creation and the first maintainer response.</summary>
        public double? ResponseDays { get; init; }
    }

    /// <summary>Result of checking for a README file.</summary>
    private sealed class GitHubReadmeData
    {
        /// <summary>Indicates whether a README file exists in the repository.</summary>
        public bool Exists { get; init; }

        /// <summary>Indicates whether the README appears to be a boilerplate default.</summary>
        public bool IsDefault { get; init; }
    }

    /// <summary>Result of checking for a CHANGELOG file.</summary>
    private sealed class GitHubChangelogData
    {
        /// <summary>Indicates whether a CHANGELOG file exists in the repository root.</summary>
        public bool Exists { get; init; }

        /// <summary>Indicates whether the CHANGELOG appears to be a boilerplate default.</summary>
        public bool IsDefault { get; init; }
    }

    /// <summary>Aggregated contributor statistics.</summary>
    private sealed class GitHubContributorData
    {
        /// <summary>The total number of non-bot contributors.</summary>
        public int ContributorCount { get; init; }

        /// <summary>The fraction of total contributions made by the top contributor.</summary>
        public double? TopContributorShare { get; init; }

        /// <summary>The fraction of total contributions made by the top two contributors combined.</summary>
        public double? TopTwoContributorShare { get; init; }
    }

    /// <summary>Aggregated CI workflow run statistics.</summary>
    private sealed class GitHubWorkflowData
    {
        /// <summary>The number of failed workflow runs among the most recent runs on the default branch.</summary>
        public int? RecentFailedWorkflowCount { get; init; }

        /// <summary>Indicates whether there is at least one recent successful workflow run on the default branch.</summary>
        public bool? HasRecentSuccessfulWorkflowRun { get; init; }

        /// <summary>The fraction of completed workflow runs that failed.</summary>
        public double? FailureRate { get; init; }

        /// <summary>Indicates whether the workflow runs show a flaky pattern (intermittent failures and successes).</summary>
        public bool? HasFlakyPattern { get; init; }
    }

    /// <summary>OpenSSF Scorecard data for the repository.</summary>
    private sealed class GitHubScorecardData
    {
        /// <summary>The aggregate OpenSSF Scorecard score.</summary>
        public double? Score { get; init; }

        /// <summary>Indicates whether the Scorecard Branch-Protection check reported a positive score.</summary>
        public bool? HasBranchProtection { get; init; }

        /// <summary>The Scorecard Binary-Artifacts check score.</summary>
        public double? BinaryArtifactsScore { get; init; }
    }

    /// <summary>Aggregated release metrics.</summary>
    private sealed class GitHubReleaseData
    {
        /// <summary>The publication date of the most recent release.</summary>
        public DateTimeOffset? LastReleaseAt { get; init; }

        /// <summary>Indicates whether releases include substantive release notes.</summary>
        public bool? HasReleaseNotes { get; init; }

        /// <summary>Indicates whether all releases use SemVer-compatible tags.</summary>
        public bool? HasSemVerReleaseTags { get; init; }

        /// <summary>The mean number of days between consecutive releases.</summary>
        public double? MeanReleaseIntervalDays { get; init; }

        /// <summary>The fraction of consecutive release transitions that were major-version bumps.</summary>
        public double? MajorReleaseRatio { get; init; }

        /// <summary>The fraction of releases marked as pre-releases.</summary>
        public double? PrereleaseRatio { get; init; }

        /// <summary>The number of release pairs published within three days of each other, indicating rapid corrections.</summary>
        public int? RapidReleaseCorrectionCount { get; init; }

        /// <summary>Indicates whether the most recent release has a verified signature.</summary>
        public bool? HasVerifiedReleaseSignature { get; init; }
    }

    /// <summary>Branch protection settings for the default branch.</summary>
    private sealed class GitHubBranchProtectionData
    {
        /// <summary>Indicates whether the default branch has protection rules enabled.</summary>
        public bool? IsProtected { get; init; }

        /// <summary>The number of required status checks configured on the default branch.</summary>
        public int? RequiredStatusCheckCount { get; init; }
    }

    /// <summary>Result of analyzing the SECURITY.md file.</summary>
    private sealed class GitHubSecurityPolicyData
    {
        /// <summary>Indicates whether a SECURITY.md file exists in the repository.</summary>
        public bool Exists { get; init; }

        /// <summary>Indicates whether the security policy contains detailed contact and reporting information.</summary>
        public bool? IsDetailed { get; init; }

        /// <summary>Indicates whether the security policy describes a coordinated disclosure process.</summary>
        public bool? HasCoordinatedDisclosure { get; init; }
    }

    /// <summary>Signals detected in workflow YAML files.</summary>
    private sealed class GitHubWorkflowFileSignals
    {
        /// <summary>Indicates whether workflow files contain a provenance attestation signal (SLSA, attest, provenance).</summary>
        public bool HasProvenanceAttestation { get; init; }

        /// <summary>Indicates whether workflow files reference a code coverage tool or service.</summary>
        public bool HasCoverageSignal { get; init; }

        /// <summary>Indicates whether workflow files reference deterministic or reproducible build tooling.</summary>
        public bool HasReproducibleBuildSignal { get; init; }

        /// <summary>Indicates whether the repository uses automated dependency update tooling (Dependabot or Renovate).</summary>
        public bool HasDependencyUpdateAutomation { get; init; }

        /// <summary>Indicates whether workflow files or repository structure suggest automated test execution.</summary>
        public bool HasTestSignal { get; init; }

        /// <summary>The number of distinct operating system platforms targeted by workflow files.</summary>
        public int PlatformCount { get; init; }
    }

    /// <summary>Commit activity and verification metrics.</summary>
    private sealed class GitHubCommitHealthData
    {
        /// <summary>The number of maintainers with at least one commit in the last six months.</summary>
        public int? RecentMaintainerCount { get; init; }

        /// <summary>The fraction of sampled commits that have a verified signature.</summary>
        public double? VerifiedCommitRatio { get; init; }

        /// <summary>The median number of days since the last commit activity for each active maintainer.</summary>
        public double? MedianMaintainerActivityDays { get; init; }
    }

    /// <summary>Lightweight snapshot of a GitHub pull request.</summary>
    private sealed class GitHubPullRequestSnapshot
    {
        /// <summary>The GitHub pull request number.</summary>
        public int Number { get; init; }

        /// <summary>The author association of the pull request author (e.g. OWNER, MEMBER, CONTRIBUTOR, NONE).</summary>
        public string? AuthorAssociation { get; init; }
    }

    /// <summary>Aggregated pull request quality metrics.</summary>
    private sealed class GitHubPullRequestQualityData
    {
        /// <summary>The fraction of recently merged pull requests authored by external contributors.</summary>
        public double? ExternalContributionRate { get; init; }

        /// <summary>The number of unique reviewers across recently merged pull requests.</summary>
        public int? UniqueReviewerCount { get; init; }

        /// <summary>The ratio of unique reviewers to total recently merged pull requests.</summary>
        public double? ReviewerDiversityRatio { get; init; }
    }
}
