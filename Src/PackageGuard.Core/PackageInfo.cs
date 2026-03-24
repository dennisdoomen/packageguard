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

    [MemoryPackIgnore]
    public bool? HasValidLicenseUrl { get; set; }

    [MemoryPackIgnore]
    public bool? IsLicensePolicyCompatible { get; set; }

    [MemoryPackIgnore]
    public int VulnerabilityCount { get; set; }

    [MemoryPackIgnore]
    public int TransitiveVulnerabilityCount { get; set; }

    [MemoryPackIgnore]
    public double MaxVulnerabilitySeverity { get; set; }

    [MemoryPackIgnore]
    public bool HasPatchedVulnerabilityInLast90Days { get; set; }

    [MemoryPackIgnore]
    public int DependencyDepth { get; set; }

    [MemoryPackIgnore]
    public bool? IsPackageSigned { get; set; }

    [MemoryPackIgnore]
    public bool? HasTrustedPackageSignature { get; set; }

    [MemoryPackIgnore]
    public DateTimeOffset? PublishedAt { get; set; }

    [MemoryPackIgnore]
    public long? DownloadCount { get; set; }

    [MemoryPackIgnore]
    public bool OwnerIsOrganization { get; set; }

    [MemoryPackIgnore]
    public DateTimeOffset? OwnerCreatedAt { get; set; }

    [MemoryPackIgnore]
    public int? ContributorCount { get; set; }

    [MemoryPackIgnore]
    public double? TopContributorShare { get; set; }

    [MemoryPackIgnore]
    public double? TopTwoContributorShare { get; set; }

    [MemoryPackIgnore]
    public bool? HasReadme { get; set; }

    [MemoryPackIgnore]
    public bool? HasDefaultReadme { get; set; }

    [MemoryPackIgnore]
    public bool? HasContributingGuide { get; set; }

    [MemoryPackIgnore]
    public bool? HasSecurityPolicy { get; set; }

    [MemoryPackIgnore]
    public bool? HasChangelog { get; set; }

    [MemoryPackIgnore]
    public bool? HasDefaultChangelog { get; set; }

    [MemoryPackIgnore]
    public int? OpenBugIssueCount { get; set; }

    [MemoryPackIgnore]
    public int? StaleCriticalBugIssueCount { get; set; }

    [MemoryPackIgnore]
    public double? MedianIssueResponseDays { get; set; }

    [MemoryPackIgnore]
    public double? MedianPullRequestMergeDays { get; set; }

    [MemoryPackIgnore]
    public bool HasPreOneZeroDependencies { get; set; }

    [MemoryPackIgnore]
    public string[] DependencyKeys { get; set; } = [];

    [MemoryPackIgnore]
    public bool HasAvailableSecurityFix { get; set; }

    [MemoryPackIgnore]
    public string? LatestStableVersion { get; set; }

    [MemoryPackIgnore]
    public bool IsMajorVersionBehindLatest { get; set; }

    [MemoryPackIgnore]
    public bool IsMinorVersionBehindLatest { get; set; }

    [MemoryPackIgnore]
    public int? RecentFailedWorkflowCount { get; set; }

    [MemoryPackIgnore]
    public bool? HasRecentSuccessfulWorkflowRun { get; set; }

    [MemoryPackIgnore]
    public double? OpenSsfScore { get; set; }

    [MemoryPackIgnore]
    public bool? HasBranchProtection { get; set; }

    [MemoryPackIgnore]
    public bool? HasProvenanceAttestation { get; set; }

    [MemoryPackIgnore]
    public bool? HasRepositoryOwnershipOrRenameChurn { get; set; }

    [MemoryPackIgnore]
    public string[] SupportedTargetFrameworks { get; set; } = [];

    [MemoryPackIgnore]
    public bool? HasModernTargetFrameworkSupport { get; set; }

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
