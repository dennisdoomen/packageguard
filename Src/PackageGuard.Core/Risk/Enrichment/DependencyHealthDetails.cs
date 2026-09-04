namespace PackageGuard.Core.Risk.Enrichment;

/// <summary>
/// Accumulates the human-readable evidence strings for the transitive dependency health issues found
/// while walking a package's dependency graph. The count for each category is the number of entries
/// collected in the corresponding list.
/// </summary>
internal sealed class DependencyHealthDetails
{
    /// <summary>
    /// Gets the descriptions of transitive dependencies whose published version is older than 24 months.
    /// </summary>
    public List<string> Stale { get; } = [];

    /// <summary>
    /// Gets the descriptions of transitive dependencies that look both stale and risky.
    /// </summary>
    public List<string> Abandoned { get; } = [];

    /// <summary>
    /// Gets the descriptions of transitive dependencies marked as deprecated by their ecosystem metadata.
    /// </summary>
    public List<string> Deprecated { get; } = [];

    /// <summary>
    /// Gets the descriptions of transitive dependencies that look both stale and critically vulnerable.
    /// </summary>
    public List<string> UnmaintainedCritical { get; } = [];
}
