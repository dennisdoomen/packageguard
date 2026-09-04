using System.Globalization;
using PackageGuard.Core.Package;
using PackageGuard.Core.Risk.Scoring;

namespace PackageGuard.Core.Risk.Enrichment;

/// <summary>
/// Enriches each <see cref="PackageInfo"/> with transitive dependency health counts and evidence: stale,
/// abandoned, deprecated, and unmaintained-critical packages reachable through the dependency graph.
/// </summary>
internal sealed class DependencyHealthCountEnricher(IReadOnlyDictionary<string, PackageInfo> packagesByKey)
    : IEnrichPackageRisk
{
    /// <summary>
    /// Returns <see langword="false"/>; transitive health counts are always recomputed from the current graph.
    /// </summary>
    public bool HasCachedData(PackageInfo package) => false;

    /// <summary>
    /// Collects the stale, abandoned, deprecated, and unmaintained-critical transitive dependencies of
    /// <paramref name="package"/> and assigns the resulting counts and evidence details to the
    /// corresponding <see cref="PackageInfo"/> properties.
    /// </summary>
    public Task EnrichAsync(PackageInfo package)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var details = new DependencyHealthDetails();
        CollectDependencyHealth(package, visited, details);

        package.StaleTransitiveDependencyCount = details.Stale.Count;
        package.StaleTransitiveDependencyDetails = details.Stale.ToArray();
        package.AbandonedTransitiveDependencyCount = details.Abandoned.Count;
        package.AbandonedTransitiveDependencyDetails = details.Abandoned.ToArray();
        package.DeprecatedTransitiveDependencyCount = details.Deprecated.Count;
        package.DeprecatedTransitiveDependencyDetails = details.Deprecated.ToArray();
        package.UnmaintainedCriticalTransitiveDependencyCount = details.UnmaintainedCritical.Count;
        package.UnmaintainedCriticalTransitiveDependencyDetails = details.UnmaintainedCritical.ToArray();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Recursively collects descriptions of the unique stale, abandoned, deprecated, and
    /// unmaintained-critical transitive dependencies of <paramref name="package"/> into
    /// <paramref name="details"/>, avoiding cycles via <paramref name="visited"/>.
    /// </summary>
    private void CollectDependencyHealth(PackageInfo package, HashSet<string> visited, DependencyHealthDetails details)
    {
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
                details.Stale.Add(FormatStaleDetail(dependency));
            }

            if (LooksAbandonedAndRisky(dependency))
            {
                details.Abandoned.Add(FormatAbandonedDetail(dependency));
            }

            if (dependency.IsDeprecated == true)
            {
                details.Deprecated.Add(FormatIdentity(dependency));
            }

            if (LooksUnmaintainedAndCritical(dependency))
            {
                details.UnmaintainedCritical.Add(FormatUnmaintainedCriticalDetail(dependency));
            }

            CollectDependencyHealth(dependency, visited, details);
        }
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

    /// <summary>
    /// Formats <paramref name="dependency"/> as "name version".
    /// </summary>
    private static string FormatIdentity(PackageInfo dependency) => $"{dependency.Name} {dependency.Version}";

    /// <summary>
    /// Formats a stale dependency as "name version (last release yyyy-MM-dd)".
    /// </summary>
    private static string FormatStaleDetail(PackageInfo dependency)
    {
        string publishedAt = dependency.PublishedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown";
        return $"{FormatIdentity(dependency)} (last release {publishedAt})";
    }

    /// <summary>
    /// Formats an abandoned/risky dependency as "name version (reason)", where reason reflects whether
    /// the dependency has known vulnerabilities, low maintainer activity, or both.
    /// </summary>
    private static string FormatAbandonedDetail(PackageInfo dependency)
    {
        bool lowMaintainerSignal = dependency.ContributorCount is null or < 2;
        bool securitySignal = dependency.VulnerabilityCount > 0 || dependency.MaxVulnerabilitySeverity >= 7.0;

        string reason = (securitySignal, lowMaintainerSignal) switch
        {
            (true, true) => "known vulnerabilities, low maintainer activity",
            (true, false) => "known vulnerabilities",
            (false, true) => "low maintainer activity",
            _ => "stale"
        };

        return $"{FormatIdentity(dependency)} ({reason})";
    }

    /// <summary>
    /// Formats an unmaintained-critical dependency as "name version (max severity X.X)".
    /// </summary>
    private static string FormatUnmaintainedCriticalDetail(PackageInfo dependency) =>
        $"{FormatIdentity(dependency)} (max severity {RiskEvaluationHelpers.FormatScore(dependency.MaxVulnerabilitySeverity)})";
}
