using System.Text.RegularExpressions;
using JetBrains.Annotations;
using MemoryPack;
using NuGet.Versioning;
using PackageGuard.Core.Common;

namespace PackageGuard.Core;

/// <summary>
/// Represents the normalized metadata, license information, and risk signals known for a single package version.
/// </summary>
[MemoryPackable]
public partial class PackageInfo
{
    /// <summary>
    /// Tracks the projects in the current analysis run that reference this package.
    /// </summary>
    private List<string> projects = new();

    /// <summary>
    /// Gets or sets the package identifier as exposed by the package ecosystem.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized package version being analyzed.
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// Gets or sets the resolved license identifier or friendly license name for the package.
    /// </summary>
    public string? License { get; set; }

    /// <summary>
    /// Gets or sets the source URL where the license text or license metadata can be retrieved.
    /// </summary>
    public string? LicenseUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the package was used in any of the
    /// projects during this run.
    /// </summary>
    /// <remarks>
    /// It is used to clear out old package information before updating the persisted cache.
    /// </remarks>
    [MemoryPackIgnore]
    public bool IsUsed { get; set; }

    /// <summary>
    /// Gets or sets the projects that reference this package in the current analysis run.
    /// </summary>
    public string[] Projects
    {
        get => projects.ToArray();
        [UsedImplicitly]
        set => projects = new List<string>();
    }

    /// <summary>
    /// Gets or sets the name of the package source that was used to fetch the metadata.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the URL of the package source that was used to fetch the metadata.
    /// </summary>
    public string SourceUrl { get; set; } = "";

    /// <summary>
    /// Gets or sets the repository URL used to enrich package metadata and risk information.
    /// </summary>
    public string? RepositoryUrl { get; set; }

    /// <summary>
    /// Gets or sets when the cached metadata for this package was last refreshed.
    /// </summary>
    public DateTimeOffset? CacheUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the overall risk score for this package (0-100, where 0 is lowest risk).
    /// </summary>
    [MemoryPackIgnore]
    public double RiskScore { get; set; }

    /// <summary>
    /// Gets or sets the individual risk dimension scores.
    /// </summary>
    [MemoryPackIgnore]
    public RiskDimensions RiskDimensions { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the discovered license URL resolved successfully.
    /// </summary>
    public bool? HasValidLicenseUrl { get; set; }

    /// <summary>
    /// Gets or sets whether license URL validation has already been attempted for this package.
    /// </summary>
    public bool HasValidatedLicenseUrl { get; set; }

    /// <summary>
    /// Gets or sets whether the package complies with the active license policy.
    /// </summary>
    [MemoryPackIgnore]
    public bool? IsLicensePolicyCompatible { get; set; }

    /// <summary>
    /// Gets or sets the number of directly reported vulnerabilities affecting this package version.
    /// </summary>
    public int VulnerabilityCount { get; set; }

    /// <summary>
    /// Gets or sets the number of vulnerabilities found through this package's dependency graph.
    /// </summary>
    [MemoryPackIgnore]
    public int TransitiveVulnerabilityCount { get; set; }

    /// <summary>
    /// Gets or sets the highest vulnerability severity recorded for this package.
    /// </summary>
    public double MaxVulnerabilitySeverity { get; set; }

    /// <summary>
    /// Gets or sets whether any known vulnerability received a fix within the last 90 days.
    /// </summary>
    public bool HasPatchedVulnerabilityInLast90Days { get; set; }

    /// <summary>
    /// Gets or sets the package depth within the analyzed dependency graph.
    /// </summary>
    [MemoryPackIgnore]
    public int DependencyDepth { get; set; }

    /// <summary>
    /// Gets or sets whether the package is signed according to the registry metadata.
    /// </summary>
    public bool? IsPackageSigned { get; set; }

    /// <summary>
    /// Gets or sets whether the package signature chains back to a trusted signer.
    /// </summary>
    public bool? HasTrustedPackageSignature { get; set; }

    /// <summary>
    /// Gets or sets whether signing metadata was collected for this package.
    /// </summary>
    public bool HasSigningRiskData { get; set; }

    /// <summary>
    /// Gets or sets whether the package has been marked as deprecated by the upstream ecosystem.
    /// </summary>
    public bool? IsDeprecated { get; set; }

    /// <summary>
    /// Gets or sets when the analyzed package version was published.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// Gets or sets the known download count reported by the package ecosystem.
    /// </summary>
    public long? DownloadCount { get; set; }

    /// <summary>
    /// Gets or sets whether the package owner appears to be an organization rather than an individual.
    /// </summary>
    public bool OwnerIsOrganization { get; set; }

    /// <summary>
    /// Gets or sets when the package owner account was created.
    /// </summary>
    public DateTimeOffset? OwnerCreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of contributors observed in the source repository.
    /// </summary>
    public int? ContributorCount { get; set; }

    /// <summary>
    /// Gets or sets the share of contributions made by the single most active contributor.
    /// </summary>
    public double? TopContributorShare { get; set; }

    /// <summary>
    /// Gets or sets the combined contribution share of the two most active contributors.
    /// </summary>
    public double? TopTwoContributorShare { get; set; }

    /// <summary>
    /// Gets or sets the number of maintainers with recent activity.
    /// </summary>
    public int? RecentMaintainerCount { get; set; }

    /// <summary>
    /// Gets or sets whether the repository contains a readme.
    /// </summary>
    public bool? HasReadme { get; set; }

    /// <summary>
    /// Gets or sets whether the repository still appears to use a default or placeholder readme.
    /// </summary>
    public bool? HasDefaultReadme { get; set; }

    /// <summary>
    /// Gets or sets when the readme was last updated.
    /// </summary>
    public DateTimeOffset? ReadmeUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets whether the repository includes a contributing guide.
    /// </summary>
    public bool? HasContributingGuide { get; set; }

    /// <summary>
    /// Gets or sets whether the repository contains any security policy.
    /// </summary>
    public bool? HasSecurityPolicy { get; set; }

    /// <summary>
    /// Gets or sets whether the repository contains a detailed security policy.
    /// </summary>
    public bool? HasDetailedSecurityPolicy { get; set; }

    /// <summary>
    /// Gets or sets whether the repository documents a coordinated vulnerability disclosure process.
    /// </summary>
    public bool? HasCoordinatedDisclosure { get; set; }

    /// <summary>
    /// Gets or sets whether the repository contains a changelog.
    /// </summary>
    public bool? HasChangelog { get; set; }

    /// <summary>
    /// Gets or sets whether the changelog appears to be a default or placeholder file.
    /// </summary>
    public bool? HasDefaultChangelog { get; set; }

    /// <summary>
    /// Gets or sets when the changelog was last updated.
    /// </summary>
    public DateTimeOffset? ChangelogUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of currently open bug issues.
    /// </summary>
    public int? OpenBugIssueCount { get; set; }

    /// <summary>
    /// Gets or sets the number of critical bug issues that have remained stale.
    /// </summary>
    public int? StaleCriticalBugIssueCount { get; set; }

    /// <summary>
    /// Gets or sets the median number of days maintainers take to respond to issues.
    /// </summary>
    public double? MedianIssueResponseDays { get; set; }

    /// <summary>
    /// Gets or sets the median number of days maintainers take to respond to critical issues.
    /// </summary>
    public double? MedianCriticalIssueResponseDays { get; set; }

    /// <summary>
    /// Gets or sets the fraction of issues for which a maintainer response was observed.
    /// </summary>
    public double? IssueResponseCoverage { get; set; }

    /// <summary>
    /// Gets or sets the median age of currently open bug issues in days.
    /// </summary>
    public double? MedianOpenBugAgeDays { get; set; }

    /// <summary>
    /// Gets or sets the number of bug issues closed during the last 90 days.
    /// </summary>
    public int? ClosedBugIssueCountLast90Days { get; set; }

    /// <summary>
    /// Gets or sets the number of bug issues reopened during the last 90 days.
    /// </summary>
    public int? ReopenedBugIssueCountLast90Days { get; set; }

    /// <summary>
    /// Gets or sets the median number of days required to merge pull requests.
    /// </summary>
    public double? MedianPullRequestMergeDays { get; set; }

    /// <summary>
    /// Gets or sets whether the package depends on pre-1.0 packages.
    /// </summary>
    public bool HasPreOneZeroDependencies { get; set; }

    /// <summary>
    /// Gets or sets the dependency keys for the package graph rooted at this package.
    /// </summary>
    [MemoryPackIgnore]
    public string[] DependencyKeys { get; set; } = [];

    /// <summary>
    /// Gets or sets whether at least one known vulnerability has an available security fix.
    /// </summary>
    public bool HasAvailableSecurityFix { get; set; }

    /// <summary>
    /// Gets or sets the median number of days required to publish vulnerability fixes.
    /// </summary>
    public double? MedianVulnerabilityFixDays { get; set; }

    /// <summary>
    /// Gets or sets whether OSV vulnerability metadata was collected for this package.
    /// </summary>
    public bool HasOsvRiskData { get; set; }

    /// <summary>
    /// Gets or sets the latest stable version known for this package.
    /// </summary>
    public string? LatestStableVersion { get; set; }

    /// <summary>
    /// Gets or sets when the latest stable version was published.
    /// </summary>
    public DateTimeOffset? LatestStablePublishedAt { get; set; }

    /// <summary>
    /// Gets or sets how many days behind the latest stable release this version is.
    /// </summary>
    public double? VersionUpdateLagDays { get; set; }

    /// <summary>
    /// Gets or sets whether the package is behind by at least one major version.
    /// </summary>
    public bool IsMajorVersionBehindLatest { get; set; }

    /// <summary>
    /// Gets or sets whether the package is behind by at least one minor or patch release within the same major version.
    /// </summary>
    public bool IsMinorVersionBehindLatest { get; set; }

    /// <summary>
    /// Gets or sets the number of failed workflow runs observed recently.
    /// </summary>
    public int? RecentFailedWorkflowCount { get; set; }

    /// <summary>
    /// Gets or sets whether a recent successful workflow run was observed.
    /// </summary>
    public bool? HasRecentSuccessfulWorkflowRun { get; set; }

    /// <summary>
    /// Gets or sets the share of recent workflow runs that failed.
    /// </summary>
    public double? WorkflowFailureRate { get; set; }

    /// <summary>
    /// Gets or sets whether recent workflows suggest a flaky pattern.
    /// </summary>
    public bool? HasFlakyWorkflowPattern { get; set; }

    /// <summary>
    /// Gets or sets the number of required status checks configured for the default branch.
    /// </summary>
    public int? RequiredStatusCheckCount { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct workflow platforms observed.
    /// </summary>
    public int? WorkflowPlatformCount { get; set; }

    /// <summary>
    /// Gets or sets whether workflow evidence suggests code coverage is collected.
    /// </summary>
    public bool? HasCoverageWorkflowSignal { get; set; }

    /// <summary>
    /// Gets or sets whether workflow evidence suggests reproducible builds are supported.
    /// </summary>
    public bool? HasReproducibleBuildSignal { get; set; }

    /// <summary>
    /// Gets or sets whether dependency update automation appears to be enabled.
    /// </summary>
    public bool? HasDependencyUpdateAutomation { get; set; }

    /// <summary>
    /// Gets or sets whether the repository shows evidence of automated or repeatable tests.
    /// </summary>
    public bool? HasTestSignal { get; set; }

    /// <summary>
    /// Gets or sets the OpenSSF score associated with the package repository.
    /// </summary>
    public double? OpenSsfScore { get; set; }

    /// <summary>
    /// Gets or sets whether branch protection appears to be enabled for the default branch.
    /// </summary>
    public bool? HasBranchProtection { get; set; }

    /// <summary>
    /// Gets or sets whether provenance attestations are published for builds or releases.
    /// </summary>
    public bool? HasProvenanceAttestation { get; set; }

    /// <summary>
    /// Gets or sets whether the repository shows signs of ownership transfer or rename churn.
    /// </summary>
    public bool? HasRepositoryOwnershipOrRenameChurn { get; set; }

    /// <summary>
    /// Gets or sets whether release artifacts are signed and verifiable.
    /// </summary>
    public bool? HasVerifiedReleaseSignature { get; set; }

    /// <summary>
    /// Gets or sets whether the package publisher identity is verified by the ecosystem.
    /// </summary>
    public bool? HasVerifiedPublisher { get; set; }

    /// <summary>
    /// Gets or sets the share of recent releases that were prerelease versions.
    /// </summary>
    public double? PrereleaseRatio { get; set; }

    /// <summary>
    /// Gets or sets the number of quick follow-up releases likely used to correct previous releases.
    /// </summary>
    public int? RapidReleaseCorrectionCount { get; set; }

    /// <summary>
    /// Gets or sets whether release notes were found for recent releases.
    /// </summary>
    public bool? HasReleaseNotes { get; set; }

    /// <summary>
    /// Gets or sets whether the repository uses semantic-versioned release tags.
    /// </summary>
    public bool? HasSemVerReleaseTags { get; set; }

    /// <summary>
    /// Gets or sets the mean number of days between releases.
    /// </summary>
    public double? MeanReleaseIntervalDays { get; set; }

    /// <summary>
    /// Gets or sets the share of releases that were major-version releases.
    /// </summary>
    public double? MajorReleaseRatio { get; set; }

    /// <summary>
    /// Gets or sets the share of contributions coming from external contributors.
    /// </summary>
    public double? ExternalContributionRate { get; set; }

    /// <summary>
    /// Gets or sets the number of unique reviewers observed on recent pull requests.
    /// </summary>
    public int? UniqueReviewerCount { get; set; }

    /// <summary>
    /// Gets or sets how evenly review work is distributed across reviewers.
    /// </summary>
    public double? ReviewerDiversityRatio { get; set; }

    /// <summary>
    /// Gets or sets the share of recent commits that were cryptographically verified.
    /// </summary>
    public double? VerifiedCommitRatio { get; set; }

    /// <summary>
    /// Gets or sets the median number of days since maintainers were last active.
    /// </summary>
    public double? MedianMaintainerActivityDays { get; set; }

    /// <summary>
    /// Gets or sets the rate at which issues are triaged within seven days.
    /// </summary>
    public double? IssueTriageWithinSevenDaysRate { get; set; }

    /// <summary>
    /// Gets or sets whether GitHub-derived risk metadata was collected for this package.
    /// </summary>
    public bool HasGitHubRiskData { get; set; }

    /// <summary>
    /// Gets or sets the target frameworks that the package explicitly supports.
    /// </summary>
    public string[] SupportedTargetFrameworks { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the package supports at least one modern target framework.
    /// </summary>
    public bool? HasModernTargetFrameworkSupport { get; set; }

    /// <summary>
    /// Gets or sets whether the package contains native or platform-specific binary assets.
    /// </summary>
    public bool? HasNativeBinaryAssets { get; set; }

    /// <summary>
    /// Gets or sets the number of stale transitive dependencies reachable from this package.
    /// </summary>
    [MemoryPackIgnore]
    public int? StaleTransitiveDependencyCount { get; set; }

    /// <summary>
    /// Gets or sets the number of abandoned transitive dependencies reachable from this package.
    /// </summary>
    [MemoryPackIgnore]
    public int? AbandonedTransitiveDependencyCount { get; set; }

    /// <summary>
    /// Gets or sets the number of deprecated transitive dependencies reachable from this package.
    /// </summary>
    [MemoryPackIgnore]
    public int? DeprecatedTransitiveDependencyCount { get; set; }

    /// <summary>
    /// Gets or sets the number of critically risky transitive dependencies considered unmaintained.
    /// </summary>
    [MemoryPackIgnore]
    public int? UnmaintainedCriticalTransitiveDependencyCount { get; set; }

    /// <summary>
    /// Determines whether this package matches the provided package name and optional version range.
    /// </summary>
    public bool SatisfiesRange(string name, string? versionRange = null)
    {
        if (!name.Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (versionRange is null)
        {
            return true;
        }

        var range = VersionRange.Parse(versionRange);
        return range.Satisfies(NuGetVersion.Parse(Version));
    }

    /// <summary>
    /// Records that the package was referenced by the specified project during this run.
    /// </summary>
    public void TrackAsUsedInProject(string projectPath)
    {
        projects.Add(projectPath);
    }

    /// <summary>
    /// Builds the cache key that uniquely identifies this package entry within a source-specific collection.
    /// </summary>
    internal string GetCollectionKey() => CreateCollectionKey(SourceUrl, Name, Version);

    /// <summary>
    /// Builds the dependency key that identifies this package across dependency graphs.
    /// </summary>
    internal string GetDependencyKey() => CreateDependencyKey(GetPackageEcosystem(), Name, Version);

    /// <summary>
    /// Builds the collection key used to distinguish packages by source URL, name, and version.
    /// </summary>
    internal static string CreateCollectionKey(string sourceUrl, string name, string version) => $"{sourceUrl}|{name}|{version}";

    /// <summary>
    /// Builds the dependency key used to distinguish packages by ecosystem, name, and version.
    /// </summary>
    internal static string CreateDependencyKey(string ecosystem, string name, string version) => $"{ecosystem}|{name}|{version}";

    /// <summary>
    /// Determines the package ecosystem used when building dependency keys.
    /// </summary>
    private string GetPackageEcosystem()
    {
        if (Source.Equals("npm", StringComparison.OrdinalIgnoreCase) ||
            SourceUrl.Contains("npmjs.org", StringComparison.OrdinalIgnoreCase) ||
            SourceUrl.Contains("yarnpkg.com", StringComparison.OrdinalIgnoreCase))
        {
            return "npm";
        }

        return "nuget";
    }

    /// <summary>
    /// Returns a concise textual representation of the package including its version and resolved license.
    /// </summary>
    public override string ToString() => $"{Name}/{Version} ({License})";

    /// <summary>
    /// Returns <c>true</c> if the name or URL of the feed where this package was found matches the given wildcard.
    /// </summary>
    public bool MatchesFeed(string feedWildcard)
    {
        return Source.MatchesWildcard(feedWildcard) || SourceUrl.MatchesWildcard(feedWildcard);
    }

    /// <summary>
    /// Marks the current package as being used by the current projects.
    /// </summary>
    public void MarkAsUsed()
    {
        IsUsed = true;
    }
}
