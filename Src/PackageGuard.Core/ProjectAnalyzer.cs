using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PackageGuard.Core.Common;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Npm;

namespace PackageGuard.Core;

/// <summary>
/// Analyzes C# projects for compliance with defined policies, such as allowed and denied packages, licenses, and feeds.
/// </summary>
public class ProjectAnalyzer(LicenseFetcher licenseFetcher, RiskEvaluator? riskEvaluator = null)
{
    /// <summary>
    /// Gets or sets the logger used to report analysis progress and diagnostics.
    /// </summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Analyzes the project at <paramref name="projectPath"/> against the configured policies and returns any violations found.
    /// </summary>
    public async Task<PolicyViolation[]> ExecuteAnalysis(string projectPath, AnalyzerSettings settings, GetPolicyByProject getPolicyByProject)
    {
        AnalysisResult result = await ExecuteAnalysisWithRisk(projectPath, settings, getPolicyByProject);
        return result.Violations;
    }

    /// <summary>
    /// Analyzes the project at <paramref name="projectPath"/> against the configured policies and returns full results,
    /// including risk metrics when <see cref="AnalyzerSettings.ReportRisk"/> is enabled.
    /// </summary>
    public async Task<AnalysisResult> ExecuteAnalysisWithRisk(string projectPath, AnalyzerSettings settings,
        GetPolicyByProject getPolicyByProject)
    {
        IProjectAnalysisStrategy[] strategies =
        [
            new CSharpProjectAnalysisStrategy(getPolicyByProject, licenseFetcher, Logger),
            new NpmProjectAnalysisStrategy(getPolicyByProject, Logger)
        ];

        List<PolicyViolation> violations = new();

        PackageInfoCollection packages = new(Logger, settings);
        if (settings is { UseCaching: true, CacheFilePath.Length: > 0 })
        {
            Logger.LogInformation("Try loading package cache from {CacheFilePath}", settings.CacheFilePath);
            await packages.TryInitializeFromCache(settings.CacheFilePath);
        }

        foreach (IProjectAnalysisStrategy strategy in strategies)
        {
            violations.AddRange(await strategy.ExecuteAnalysis(projectPath, settings, packages));
        }

        PackageInfo[] allPackages = packages.GetAllUsedPackages();
        if (settings.ReportRisk)
        {
            Logger.LogHeader("Collecting risk metadata");
            Logger.LogInformation(
                "Building risk report data for {PackageCount} packages. This can take a while while repository, release, and security signals are refreshed.",
                allPackages.Length);

            var enricher = new PackageRiskEnricher(Logger, settings.GitHubApiKey);
            await enricher.EnrichAsync(allPackages);
            PopulateTransitiveVulnerabilityCounts(allPackages);
            PopulateDependencyHealthCounts(allPackages);

            Logger.LogInformation("Risk metadata collection complete. Calculating package risk scores.");

            RiskEvaluator evaluator = riskEvaluator ?? new RiskEvaluator(Logger);
            foreach (PackageInfo package in allPackages)
            {
                evaluator.EvaluateRisk(package);
            }

            Logger.LogInformation("Risk scoring complete for {PackageCount} packages.", allPackages.Length);
        }

        if (settings.UseCaching)
        {
            await packages.WriteToCache(settings.CacheFilePath);
        }

        return new AnalysisResult
        {
            Violations = violations.ToArray(),
            Packages = allPackages
        };
    }

    /// <summary>
    /// Iterates all packages and sets <see cref="PackageInfo.TransitiveVulnerabilityCount"/> to the number
    /// of unique vulnerable packages reachable through the dependency graph.
    /// </summary>
    private static void PopulateTransitiveVulnerabilityCounts(PackageInfo[] packages)
    {
        Dictionary<string, PackageInfo> packagesByKey = CreatePackagesByKey(packages);

        foreach (PackageInfo package in packages)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = CountVulnerableDependencies(package, packagesByKey, visited);
            package.TransitiveVulnerabilityCount = count;
        }
    }

    /// <summary>
    /// Iterates all packages and sets transitive dependency health counts (stale, abandoned, deprecated,
    /// unmaintained-critical) by walking the dependency graph for each package.
    /// </summary>
    private static void PopulateDependencyHealthCounts(PackageInfo[] packages)
    {
        Dictionary<string, PackageInfo> packagesByKey = CreatePackagesByKey(packages);

        foreach (PackageInfo package in packages)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            (int staleCount, int abandonedCount, int deprecatedCount, int unmaintainedCriticalCount) =
                CountDependencyHealth(package, packagesByKey, visited);
            package.StaleTransitiveDependencyCount = staleCount;
            package.AbandonedTransitiveDependencyCount = abandonedCount;
            package.DeprecatedTransitiveDependencyCount = deprecatedCount;
            package.UnmaintainedCriticalTransitiveDependencyCount = unmaintainedCriticalCount;
        }
    }

    /// <summary>
    /// Recursively counts the number of unique transitive dependencies of <paramref name="package"/> that have
    /// at least one known vulnerability, avoiding cycles via <paramref name="visited"/>.
    /// </summary>
    private static int CountVulnerableDependencies(PackageInfo package, IReadOnlyDictionary<string, PackageInfo> packagesByKey,
        HashSet<string> visited)
    {
        int count = 0;

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

            if (dependency.VulnerabilityCount > 0)
            {
                count++;
            }

            count += CountVulnerableDependencies(dependency, packagesByKey, visited);
        }

        return count;
    }

    /// <summary>
    /// Recursively counts the number of unique stale, abandoned, deprecated, and unmaintained-critical
    /// transitive dependencies of <paramref name="package"/>, avoiding cycles via <paramref name="visited"/>.
    /// </summary>
    private static (int staleCount, int abandonedCount, int deprecatedCount, int unmaintainedCriticalCount) CountDependencyHealth(PackageInfo package,
        IReadOnlyDictionary<string, PackageInfo> packagesByKey, HashSet<string> visited)
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
                CountDependencyHealth(dependency, packagesByKey, visited);
            staleCount += nestedStale;
            abandonedCount += nestedAbandoned;
            deprecatedCount += nestedDeprecated;
            unmaintainedCriticalCount += nestedUnmaintainedCritical;
        }

        return (staleCount, abandonedCount, deprecatedCount, unmaintainedCriticalCount);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="dependency"/> was published more than 24 months ago.
    /// </summary>
    private static bool IsStaleDependency(PackageInfo dependency) =>
        dependency.PublishedAt != null && dependency.PublishedAt.Value < DateTimeOffset.UtcNow.AddMonths(-24);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="dependency"/> is stale and also shows low maintainer activity
    /// or has known security vulnerabilities with high severity.
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
    /// Returns <c>true</c> when <paramref name="dependency"/> is stale and has high-severity unpatched vulnerabilities.
    /// </summary>
    private static bool LooksUnmaintainedAndCritical(PackageInfo dependency) =>
        IsStaleDependency(dependency) &&
        dependency is { MaxVulnerabilitySeverity: >= 7.0, VulnerabilityCount: > 0 };

    /// <summary>
    /// Builds a dictionary keyed by each package's dependency key for fast lookup during graph traversal.
    /// </summary>
    private static Dictionary<string, PackageInfo> CreatePackagesByKey(IEnumerable<PackageInfo> packages) =>
        packages
            .GroupBy(CreatePackageKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the dependency key for <paramref name="package"/> used as the lookup key in the packages-by-key dictionary.
    /// </summary>
    private static string CreatePackageKey(PackageInfo package) => package.GetDependencyKey();
}
