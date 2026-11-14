using System.Text.Json;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Package;
using PackageGuard.Core.Risk.Enrichment.GitHub;
using static PackageGuard.Core.Common.JsonElementReader;
using static PackageGuard.Core.Risk.Enrichment.GitHub.GitHubRequestTracker;

namespace PackageGuard.Core.Risk.Enrichment;

/// <summary>Enriches package risk data using GitHub repository API metadata.</summary>
internal sealed class GitHubRepositoryRiskEnricher : IEnrichPackageRisk
{
    /// <summary>In-flight load tasks per repository API root, preventing duplicate concurrent fetches.</summary>
    private static readonly Dictionary<string, Task<Tracked<GitHubRepositoryRiskData?>>> InFlightLoads =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lock for thread-safe access to the in-flight loads.</summary>
    private static readonly Lock CacheLock = new();

    private readonly ILogger logger;
    private readonly GitHubApiClient apiClient;
    private readonly GitHubRepositoryRiskCache repositoryCache;
    private readonly GitHubRepositoryFiles files;
    private readonly GitHubReleaseCollector releases;
    private readonly GitHubReadmeCollector readme;
    private readonly GitHubActivityCollector activity;
    private readonly GitHubContributorCollector contributors;
    private readonly GitHubCommitHealthCollector commitHealth;
    private readonly GitHubWorkflowRunCollector workflowRuns;
    private readonly GitHubBranchProtectionCollector branchProtection;
    private readonly GitHubScorecardCollector scorecard;
    private readonly GitHubChangelogCollector changelog;
    private readonly GitHubSecurityPolicyCollector securityPolicy;
    private readonly GitHubWorkflowFileCollector workflowFiles;

    /// <summary>Creates an enricher that talks to GitHub through the client shared by the whole run.</summary>
    /// <param name="logger">The logger to report fetch problems to.</param>
    /// <param name="gitHubApiKey">The GitHub personal access token to authenticate with, if any.</param>
    public GitHubRepositoryRiskEnricher(ILogger logger, string? gitHubApiKey)
        : this(logger, GitHubApi.GetOrCreateClient(logger, gitHubApiKey), GitHubApi.GetOrCreateRepositoryCache(logger))
    {
    }

    /// <summary>Creates an enricher that talks to GitHub through the given client.</summary>
    /// <param name="logger">The logger to report fetch problems to.</param>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    /// <param name="repositoryCache">The cache of repository profiles to read from and add to.</param>
    internal GitHubRepositoryRiskEnricher(ILogger logger, GitHubApiClient apiClient,
        GitHubRepositoryRiskCache? repositoryCache = null)
    {
        this.logger = logger;
        this.apiClient = apiClient;
        this.repositoryCache = repositoryCache ?? new GitHubRepositoryRiskCache(logger);
        files = new GitHubRepositoryFiles(apiClient);
        releases = new GitHubReleaseCollector(apiClient);
        readme = new GitHubReadmeCollector(apiClient);
        activity = new GitHubActivityCollector(apiClient, logger);
        contributors = new GitHubContributorCollector(apiClient);
        commitHealth = new GitHubCommitHealthCollector(apiClient);
        workflowRuns = new GitHubWorkflowRunCollector(apiClient);
        branchProtection = new GitHubBranchProtectionCollector(apiClient);
        scorecard = new GitHubScorecardCollector(logger);
        changelog = new GitHubChangelogCollector(apiClient);
        securityPolicy = new GitHubSecurityPolicyCollector(apiClient);
        workflowFiles = new GitHubWorkflowFileCollector(apiClient);
    }

    /// <summary>Discards the in-flight repository loads. Only used by the tests.</summary>
    internal static void ClearCache()
    {
        lock (CacheLock)
        {
            InFlightLoads.Clear();
        }
    }

    /// <summary>Returns true if GitHub risk data is already populated for the package.</summary>
    public bool HasCachedData(PackageInfo package) => package.HasGitHubRiskData;

    /// <summary>Fetches GitHub repository risk data and applies it to the package.</summary>
    public async Task EnrichAsync(PackageInfo package)
    {
        string? repositoryApiRoot = GitHubRepositoryUrl.TryGetApiRoot(package.RepositoryUrl);
        if (repositoryApiRoot is null)
        {
            return;
        }

        Tracked<GitHubRepositoryRiskData?> load = await GetRepositoryRiskDataAsync(repositoryApiRoot);
        package.HasIncompleteGitHubRiskData = !load.IsComplete;
        if (load.Value is null)
        {
            return;
        }

        ApplyOwnerAndDocumentationData(package, load.Value);
        ApplyActivityAndWorkflowData(package, load.Value);
        package.HasGitHubRiskData = load.IsComplete;
    }

    /// <summary>Applies owner, contributor, documentation, and issue data from cached GitHub data.</summary>
    private static void ApplyOwnerAndDocumentationData(PackageInfo package, GitHubRepositoryRiskData cached)
    {
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
    }

    /// <summary>Applies workflow, supply-chain, and release data from cached GitHub data.</summary>
    private static void ApplyActivityAndWorkflowData(PackageInfo package, GitHubRepositoryRiskData cached)
    {
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
    }

    /// <summary>
    /// Returns cached or loads risk data for the given API root, caching it only when every request got an answer.
    /// An incomplete profile leaves the signals it could not collect empty, so that the next run tries again.
    /// </summary>
    private async Task<Tracked<GitHubRepositoryRiskData?>> GetRepositoryRiskDataAsync(string repositoryApiRoot)
    {
        Task<Tracked<GitHubRepositoryRiskData?>> loadTask;

        if (repositoryCache.TryGet(repositoryApiRoot, out GitHubRepositoryRiskData? cached))
        {
            return new Tracked<GitHubRepositoryRiskData?>(cached, IsComplete: true);
        }

        lock (CacheLock)
        {
            if (!InFlightLoads.TryGetValue(repositoryApiRoot, out loadTask!))
            {
                loadTask = TrackAsync(() => TryLoadDataAsync(repositoryApiRoot));
                InFlightLoads[repositoryApiRoot] = loadTask;
            }
        }

        Tracked<GitHubRepositoryRiskData?> loaded = await loadTask;

        lock (CacheLock)
        {
            InFlightLoads.Remove(repositoryApiRoot);
        }

        if (loaded.IsComplete)
        {
            repositoryCache.Store(repositoryApiRoot, loaded.Value);
        }
        else
        {
            logger.LogDebug("Not caching the incomplete GitHub repository profile of {RepositoryApiRoot}",
                repositoryApiRoot);
        }

        return loaded;
    }

    /// <summary>Fetches all GitHub data for the repository and assembles a GitHubRepositoryRiskData record.</summary>
    private async Task<GitHubRepositoryRiskData?> TryLoadDataAsync(string repositoryApiRoot)
    {
        try
        {
            GitHubRepositoryContext? repository = await TryReadRepositoryContextAsync(repositoryApiRoot);
            return repository is null ? null : await FetchAndAssembleRepositoryDataAsync(repository);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch GitHub repository risk metadata from {RepositoryApiRoot}", repositoryApiRoot);
            GitHubRequestFailureScope.ReportFailure();
            return null;
        }
    }

    /// <summary>
    /// Reads the repository and its owner, or returns <see langword="null"/> when the repository could not be found.
    /// </summary>
    private async Task<GitHubRepositoryContext?> TryReadRepositoryContextAsync(string repositoryApiRoot)
    {
        using JsonDocument? repoDocument =
            await apiClient.GetJsonAsync(repositoryApiRoot, fields: GitHubResponseFields.Repository);
        if (repoDocument is null)
        {
            return null;
        }

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

        DateTimeOffset? ownerCreatedAt = await TryGetOwnerCreatedAtAsync(ownerLogin, ownerIsOrganization);

        return new GitHubRepositoryContext(repositoryApiRoot, defaultBranch, ownerLogin, repositoryName,
            ownerIsOrganization, canonicalUrl, ownerCreatedAt);
    }

    /// <summary>Reads when the user or organization that owns the repository created its account.</summary>
    private async Task<DateTimeOffset?> TryGetOwnerCreatedAtAsync(string ownerLogin, bool ownerIsOrganization)
    {
        using JsonDocument? ownerDocument = await apiClient.GetJsonAsync(ownerIsOrganization
                ? $"https://api.github.com/orgs/{ownerLogin}"
                : $"https://api.github.com/users/{ownerLogin}",
            fields: GitHubResponseFields.Of("created_at"));

        return ownerDocument is null
            ? null
            : TryReadDate(ownerDocument.RootElement, "created_at");
    }

    /// <summary>Runs all signal collectors and assembles their results into a GitHubRepositoryRiskData record.</summary>
    private async Task<GitHubRepositoryRiskData> FetchAndAssembleRepositoryDataAsync(GitHubRepositoryContext repository)
    {
        GitHubRepositoryWideSignals repositoryWide = StartRepositoryWideCollectors(repository);

        Tracked<string[]> rootFiles = await repositoryWide.RootFiles;
        GitHubRootFileSignals rootFileBased = StartRootFileCollectors(repository, rootFiles.Value);

        await Task.WhenAll(repositoryWide.WhenAllAsync(), rootFileBased.WhenAllAsync());

        return await GitHubRepositoryRiskDataAssembler.AssembleAsync(repository, repositoryWide, rootFileBased);
    }

    /// <summary>Starts the collectors that only need the repository itself, including the root listing.</summary>
    private GitHubRepositoryWideSignals StartRepositoryWideCollectors(GitHubRepositoryContext repository) => new()
    {
        Releases = releases.CollectAsync(repository),
        Readme = TrackAsync(() => readme.CollectAsync(repository)),
        RootFiles = TrackAsync(() => files.GetRootFilesAsync(repository)),
        Activity = activity.CollectAsync(repository),
        Contributors = TrackAsync(() => contributors.CollectAsync(repository)),
        CommitHealth = commitHealth.CollectAsync(repository),
        WorkflowRuns = workflowRuns.CollectAsync(repository),
        BranchProtection = branchProtection.CollectAsync(repository),
        Scorecard = scorecard.CollectAsync(repository)
    };

    /// <summary>Starts the collectors that decide from the root listing which files to read.</summary>
    private GitHubRootFileSignals StartRootFileCollectors(GitHubRepositoryContext repository, string[] rootFiles) => new()
    {
        Changelog = TrackAsync(() => changelog.CollectAsync(repository, rootFiles)),
        SecurityPolicy = TrackAsync(() => securityPolicy.CollectAsync(repository, rootFiles)),
        WorkflowFiles = workflowFiles.CollectAsync(repository, rootFiles),
        ReadmeUpdatedAt = readme.GetUpdatedAtAsync(repository, rootFiles),
        ChangelogUpdatedAt = changelog.GetUpdatedAtAsync(repository, rootFiles)
    };

    /// <summary>Returns true if the declared and canonical GitHub repo identifiers differ.</summary>
    private static bool HasRepositoryOwnershipOrRenameChurn(string? declaredRepositoryUrl, string canonicalUrl)
    {
        string? declaredIdentifier = GitHubRepositoryUrl.TryGetIdentifier(declaredRepositoryUrl);
        string? canonicalIdentifier = GitHubRepositoryUrl.TryGetIdentifier(canonicalUrl);
        return !string.IsNullOrWhiteSpace(canonicalIdentifier) &&
               !string.IsNullOrWhiteSpace(declaredIdentifier) &&
               !canonicalIdentifier.Equals(declaredIdentifier, StringComparison.OrdinalIgnoreCase);
    }
}
