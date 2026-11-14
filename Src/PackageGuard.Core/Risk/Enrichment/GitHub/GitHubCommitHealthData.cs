namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Commit activity and verification metrics.</summary>
internal sealed class GitHubCommitHealthData
{
    /// <summary>The number of maintainers with at least one commit in the last six months.</summary>
    public int? RecentMaintainerCount { get; init; }

    /// <summary>The fraction of sampled commits that have a verified signature.</summary>
    public double? VerifiedCommitRatio { get; init; }

    /// <summary>The median number of days since the last commit activity for each active maintainer.</summary>
    public double? MedianMaintainerActivityDays { get; init; }
}
