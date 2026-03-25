using System.Text.RegularExpressions;
using JetBrains.Annotations;
using MemoryPack;
using NuGet.Versioning;
using PackageGuard.Core.Common;

namespace PackageGuard.Core;

[MemoryPackable]
public partial class PackageInfo
{
    private List<string> projects = new();

    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? License { get; set; }
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

    public string[] Projects
    {
        get => projects.ToArray();
        [UsedImplicitly]
        set => projects = new List<string>();
    }

    /// <summary>
    /// Gets or sets the name of the NuGet source that was used to fetch the metadata.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the Url of the NuGet source that was used to fetch the metadata.
    /// </summary>
    public string SourceUrl { get; set; } = "";

    public string? RepositoryUrl { get; set; }

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

    public bool? HasValidLicenseUrl { get; set; }

    public bool HasValidatedLicenseUrl { get; set; }

    [MemoryPackIgnore]
    public bool? IsLicensePolicyCompatible { get; set; }

    public int VulnerabilityCount { get; set; }

    [MemoryPackIgnore]
    public int TransitiveVulnerabilityCount { get; set; }

    public double MaxVulnerabilitySeverity { get; set; }

    public bool HasPatchedVulnerabilityInLast90Days { get; set; }

    [MemoryPackIgnore]
    public int DependencyDepth { get; set; }

    public bool? IsPackageSigned { get; set; }

    public bool? HasTrustedPackageSignature { get; set; }

    public bool HasSigningRiskData { get; set; }

    public bool? IsDeprecated { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public long? DownloadCount { get; set; }

    public bool OwnerIsOrganization { get; set; }

    public DateTimeOffset? OwnerCreatedAt { get; set; }

    public int? ContributorCount { get; set; }

    public double? TopContributorShare { get; set; }

    public double? TopTwoContributorShare { get; set; }

    public int? RecentMaintainerCount { get; set; }

    public bool? HasReadme { get; set; }

    public bool? HasDefaultReadme { get; set; }

    public DateTimeOffset? ReadmeUpdatedAt { get; set; }

    public bool? HasContributingGuide { get; set; }

    public bool? HasSecurityPolicy { get; set; }

    public bool? HasDetailedSecurityPolicy { get; set; }

    public bool? HasCoordinatedDisclosure { get; set; }

    public bool? HasChangelog { get; set; }

    public bool? HasDefaultChangelog { get; set; }

    public DateTimeOffset? ChangelogUpdatedAt { get; set; }

    public int? OpenBugIssueCount { get; set; }

    public int? StaleCriticalBugIssueCount { get; set; }

    public double? MedianIssueResponseDays { get; set; }

    public double? MedianCriticalIssueResponseDays { get; set; }

    public double? IssueResponseCoverage { get; set; }

    public double? MedianOpenBugAgeDays { get; set; }

    public int? ClosedBugIssueCountLast90Days { get; set; }

    public int? ReopenedBugIssueCountLast90Days { get; set; }

    public double? MedianPullRequestMergeDays { get; set; }

    public bool HasPreOneZeroDependencies { get; set; }

    [MemoryPackIgnore]
    public string[] DependencyKeys { get; set; } = [];

    public bool HasAvailableSecurityFix { get; set; }

    public double? MedianVulnerabilityFixDays { get; set; }

    public bool HasOsvRiskData { get; set; }

    public string? LatestStableVersion { get; set; }

    public DateTimeOffset? LatestStablePublishedAt { get; set; }

    public double? VersionUpdateLagDays { get; set; }

    public bool IsMajorVersionBehindLatest { get; set; }

    public bool IsMinorVersionBehindLatest { get; set; }

    public int? RecentFailedWorkflowCount { get; set; }

    public bool? HasRecentSuccessfulWorkflowRun { get; set; }

    public double? WorkflowFailureRate { get; set; }

    public bool? HasFlakyWorkflowPattern { get; set; }

    public int? RequiredStatusCheckCount { get; set; }

    public int? WorkflowPlatformCount { get; set; }

    public bool? HasCoverageWorkflowSignal { get; set; }

    public bool? HasReproducibleBuildSignal { get; set; }

    public bool? HasDependencyUpdateAutomation { get; set; }

    public bool? HasTestSignal { get; set; }

    public double? OpenSsfScore { get; set; }

    public bool? HasBranchProtection { get; set; }

    public bool? HasProvenanceAttestation { get; set; }

    public bool? HasRepositoryOwnershipOrRenameChurn { get; set; }

    public bool? HasVerifiedReleaseSignature { get; set; }

    public bool? HasVerifiedPublisher { get; set; }

    public double? PrereleaseRatio { get; set; }

    public int? RapidReleaseCorrectionCount { get; set; }

    public bool? HasReleaseNotes { get; set; }

    public bool? HasSemVerReleaseTags { get; set; }

    public double? MeanReleaseIntervalDays { get; set; }

    public double? MajorReleaseRatio { get; set; }

    public double? ExternalContributionRate { get; set; }

    public int? UniqueReviewerCount { get; set; }

    public double? ReviewerDiversityRatio { get; set; }

    public double? VerifiedCommitRatio { get; set; }

    public double? MedianMaintainerActivityDays { get; set; }

    public double? IssueTriageWithinSevenDaysRate { get; set; }

    public bool HasGitHubRiskData { get; set; }

    public string[] SupportedTargetFrameworks { get; set; } = [];

    public bool? HasModernTargetFrameworkSupport { get; set; }

    public bool? HasNativeBinaryAssets { get; set; }

    [MemoryPackIgnore]
    public int? StaleTransitiveDependencyCount { get; set; }

    [MemoryPackIgnore]
    public int? AbandonedTransitiveDependencyCount { get; set; }

    [MemoryPackIgnore]
    public int? DeprecatedTransitiveDependencyCount { get; set; }

    [MemoryPackIgnore]
    public int? UnmaintainedCriticalTransitiveDependencyCount { get; set; }

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

    public void TrackAsUsedInProject(string projectPath)
    {
        projects.Add(projectPath);
    }

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

