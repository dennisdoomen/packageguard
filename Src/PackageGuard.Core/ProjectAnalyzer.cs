using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Npm;

namespace PackageGuard.Core;

/// <summary>
/// Analyzes C# projects for compliance with defined policies, such as allowed and denied packages, licenses, and feeds.
/// </summary>
public class ProjectAnalyzer(LicenseFetcher licenseFetcher, RiskEvaluator? riskEvaluator = null)
{
    public ILogger Logger { get; set; } = NullLogger.Instance;

    public async Task<PolicyViolation[]> ExecuteAnalysis(string projectPath, AnalyzerSettings settings, GetPolicyByProject getPolicyByProject)
    {
        AnalysisResult result = await ExecuteAnalysisWithRisk(projectPath, settings, getPolicyByProject);
        return result.Violations;
    }

    public async Task<AnalysisResult> ExecuteAnalysisWithRisk(string projectPath, AnalyzerSettings settings,
        GetPolicyByProject getPolicyByProject)
    {
        IProjectAnalysisStrategy[] strategies =
        [
            new CSharpProjectAnalysisStrategy(getPolicyByProject, licenseFetcher, Logger),
            new NpmProjectAnalysisStrategy(getPolicyByProject, Logger)
        ];

        List<PolicyViolation> violations = new();

        PackageInfoCollection packages = new(Logger);
        if (settings.UseCaching && settings.CacheFilePath.Length > 0)
        {
            Logger.LogInformation("Try loading package cache from {CacheFilePath}", settings.CacheFilePath);
            await packages.TryInitializeFromCache(settings.CacheFilePath);
        }

        foreach (IProjectAnalysisStrategy strategy in strategies)
        {
            violations.AddRange(await strategy.ExecuteAnalysis(projectPath, settings, packages));
        }

        if (settings.UseCaching)
        {
            await packages.WriteToCache(settings.CacheFilePath);
        }

        PackageInfo[] allPackages = packages.GetAllUsedPackages();
        if (settings.ReportRisk)
        {
            var enricher = new PackageRiskEnricher(Logger, settings.GitHubApiKey);
            await enricher.EnrichAsync(allPackages);
            PopulateTransitiveVulnerabilityCounts(allPackages);
            PopulateDependencyHealthCounts(allPackages);

            RiskEvaluator evaluator = riskEvaluator ?? new RiskEvaluator(Logger);
            foreach (PackageInfo package in allPackages)
            {
                evaluator.EvaluateRisk(package);
            }
        }

        return new AnalysisResult
        {
            Violations = violations.ToArray(),
            Packages = allPackages
        };
    }

    private static void PopulateTransitiveVulnerabilityCounts(PackageInfo[] packages)
    {
        Dictionary<string, PackageInfo> packagesByKey = packages.ToDictionary(CreatePackageKey, StringComparer.OrdinalIgnoreCase);

        foreach (PackageInfo package in packages)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = CountVulnerableDependencies(package, packagesByKey, visited);
            package.TransitiveVulnerabilityCount = count;
        }
    }

    private static void PopulateDependencyHealthCounts(PackageInfo[] packages)
    {
        Dictionary<string, PackageInfo> packagesByKey = packages.ToDictionary(CreatePackageKey, StringComparer.OrdinalIgnoreCase);

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

    private static bool IsStaleDependency(PackageInfo dependency) =>
        dependency.PublishedAt is DateTimeOffset publishedAt && publishedAt < DateTimeOffset.UtcNow.AddMonths(-24);

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

    private static bool LooksUnmaintainedAndCritical(PackageInfo dependency) =>
        IsStaleDependency(dependency) &&
        dependency.MaxVulnerabilitySeverity >= 7.0 &&
        dependency.VulnerabilityCount > 0;

    private static string CreatePackageKey(PackageInfo package) =>
        $"{package.Source}|{package.Name}|{package.Version}";
}
