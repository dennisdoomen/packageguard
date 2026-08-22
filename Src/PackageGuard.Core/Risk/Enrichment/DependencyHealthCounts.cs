namespace PackageGuard.Core.Risk.Enrichment;

/// <summary>
/// Holds the counts of transitive dependency health issues for a package.
/// </summary>
internal sealed record DependencyHealthCounts(
    int StaleCount,
    int AbandonedCount,
    int DeprecatedCount,
    int UnmaintainedCriticalCount);
