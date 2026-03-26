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
            await BuildRiskReport(settings, packages);
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

    private async Task BuildRiskReport(AnalyzerSettings settings, PackageInfoCollection packages)
    {
        PackageInfo[] allPackages = packages.GetAllUsedPackages();

        Logger.LogHeader("Collecting risk metadata");

        Logger.LogInformation(
            "Building risk report data for {PackageCount} packages. This can take a while while repository, release, and security signals are refreshed.",
            allPackages.Length);

        var enricher = new ParallelPackageRiskEnricher(Logger, settings.GitHubApiKey);
        await enricher.EnrichAsync(allPackages);

        IReadOnlyDictionary<string, PackageInfo> packagesByKey = packages.CreatePackagesByKey();
        var transitiveVulnEnricher = new TransitiveVulnerabilityCountEnricher(packagesByKey);
        var healthEnricher = new DependencyHealthCountEnricher(packagesByKey);

        foreach (PackageInfo package in allPackages)
        {
            await transitiveVulnEnricher.EnrichAsync(package);
            await healthEnricher.EnrichAsync(package);
        }

        Logger.LogInformation("Risk metadata collection complete. Calculating package risk scores.");

        RiskEvaluator evaluator = riskEvaluator ?? new RiskEvaluator(Logger);
        foreach (PackageInfo package in allPackages)
        {
            evaluator.EvaluateRisk(package);
        }

        Logger.LogInformation("Risk scoring complete for {PackageCount} packages.", allPackages.Length);
    }

}
