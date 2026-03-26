namespace PackageGuard.Core;

/// <summary>
/// Enriches each <see cref="PackageInfo"/> with transitive dependency health counts: stale, abandoned,
/// deprecated, and unmaintained-critical packages reachable through the dependency graph.
/// </summary>
internal sealed class DependencyHealthCountEnricher(IReadOnlyDictionary<string, PackageInfo> packagesByKey)
    : IEnrichPackageRisk
{
    /// <summary>
    /// Returns <see langword="false"/>; transitive health counts are always recomputed from the current graph.
    /// </summary>
    public bool HasCachedData(PackageInfo package) => false;

    /// <summary>
    /// Counts the stale, abandoned, deprecated, and unmaintained-critical transitive dependencies of
    /// <paramref name="package"/> and assigns the results to the corresponding
    /// <see cref="PackageInfo"/> properties.
    /// </summary>
    public Task EnrichAsync(PackageInfo package)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        (int staleCount, int abandonedCount, int deprecatedCount, int unmaintainedCriticalCount) =
            CountDependencyHealth(package, visited);

        package.StaleTransitiveDependencyCount = staleCount;
        package.AbandonedTransitiveDependencyCount = abandonedCount;
        package.DeprecatedTransitiveDependencyCount = deprecatedCount;
        package.UnmaintainedCriticalTransitiveDependencyCount = unmaintainedCriticalCount;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Recursively counts the number of unique stale, abandoned, deprecated, and unmaintained-critical
    /// transitive dependencies of <paramref name="package"/>, avoiding cycles via <paramref name="visited"/>.
    /// </summary>
    private (int staleCount, int abandonedCount, int deprecatedCount, int unmaintainedCriticalCount) CountDependencyHealth(
        PackageInfo package, HashSet<string> visited)
    {
        int staleCount = 0;
        int abandonedCount = 0;
        int deprecatedCount = 0;
        int unmaintainedCriticalCount = 0;

        foreach (string dependencyKey in package.DependencyKeys)
        {
            if (!visited.Add(dependencyKey))
            {
                continue;
            }

            if (!packagesByKey.TryGetValue(dependencyKey, out PackageInfo? dependency))
            {
                continue;
            }

            if (IsStaleDependency(dependency))
            {
                staleCount++;
            }

            if (LooksAbandonedAndRisky(dependency))
            {
                abandonedCount++;
            }

            if (dependency.IsDeprecated == true)
            {
                deprecatedCount++;
            }

            if (LooksUnmaintainedAndCritical(dependency))
            {
                unmaintainedCriticalCount++;
            }

            (int nestedStale, int nestedAbandoned, int nestedDeprecated, int nestedUnmaintainedCritical) =
                CountDependencyHealth(dependency, visited);
            staleCount += nestedStale;
            abandonedCount += nestedAbandoned;
            deprecatedCount += nestedDeprecated;
            unmaintainedCriticalCount += nestedUnmaintainedCritical;
        }

        return (staleCount, abandonedCount, deprecatedCount, unmaintainedCriticalCount);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="dependency"/> was published more than 24 months ago.
    /// </summary>
    private static bool IsStaleDependency(PackageInfo dependency) =>
        dependency.PublishedAt != null && dependency.PublishedAt.Value < DateTimeOffset.UtcNow.AddMonths(-24);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="dependency"/> is stale and also shows low maintainer
    /// activity or has known security vulnerabilities with high severity.
    /// </summary>
    private static bool LooksAbandonedAndRisky(PackageInfo dependency)
    {
        if (!IsStaleDependency(dependency))
        {
            return false;
        }

        bool lowMaintainerSignal = dependency.ContributorCount is null or < 2;
        bool securitySignal = dependency.VulnerabilityCount > 0 || dependency.MaxVulnerabilitySeverity >= 7.0;
        return lowMaintainerSignal || securitySignal;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="dependency"/> is stale and has high-severity
    /// unpatched vulnerabilities.
    /// </summary>
    private static bool LooksUnmaintainedAndCritical(PackageInfo dependency) =>
        IsStaleDependency(dependency) &&
        dependency is { MaxVulnerabilitySeverity: >= 7.0, VulnerabilityCount: > 0 };
}
