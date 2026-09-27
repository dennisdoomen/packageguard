using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.ProjectModel;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Npm;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;
using PackageGuard.Core.Risk;

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
    /// Gets or sets an optional callback invoked with each C# project's path and restored lock file as it
    /// is loaded during analysis. Lets callers (such as the <c>explain</c> command) inspect the resolved
    /// dependency graph without triggering a second restore.
    /// </summary>
    public Action<string, LockFile>? OnProjectLockFileLoaded { get; set; }

    /// <summary>
    /// Analyzes the project at <paramref name="projectPath"/> against the configured policies and returns any violations found.
    /// </summary>
    public async Task<PolicyViolation[]> ExecuteAnalysis(string projectPath, AnalyzerSettings settings,
        GetPolicyByProject getPolicyByProject)
    {
        AnalysisResult result = await ExecuteAnalysisWithRisk(projectPath, settings, getPolicyByProject);
        return result.Violations;
    }

    /// <summary>
    /// Analyzes the project at <paramref name="projectPath"/> against the configured policies and returns full results,
    /// including risk metrics when <see cref="AnalyzerSettings.ReportRisk"/> is enabled.
    /// </summary>
    /// <param name="projectPath">The project or solution path to analyze.</param>
    /// <param name="settings">The analyzer settings, including whether risk metrics are requested.</param>
    /// <param name="getPolicyByProject">Resolves the effective policy for a given project path.</param>
    /// <param name="selectPackagesForRiskScoring">
    /// Optional filter narrowing which packages receive a risk score, given every package used in this run.
    /// When provided, only the returned packages (plus their own transitive dependencies, since risk factors
    /// such as transitive vulnerability counts need those enriched too) have their risk signals fetched and
    /// scored — everything else is skipped. Defaults to <see langword="null"/>, which scores every package,
    /// matching the behavior of a full <c>--report-risk</c> run.
    /// </param>
    public async Task<AnalysisResult> ExecuteAnalysisWithRisk(string projectPath, AnalyzerSettings settings,
        GetPolicyByProject getPolicyByProject, Func<PackageInfo[], PackageInfo[]>? selectPackagesForRiskScoring = null)
    {
        // An unspecified path means "the current directory" to every strategy below, but several of their
        // file-path helpers throw on an empty string rather than treating it that way, so normalize once here.
        string effectiveProjectPath = string.IsNullOrEmpty(projectPath) ? "." : projectPath;

        PackageInfoCollection packages = new(Logger, settings);
        var cache = new PackageCacheStore(Logger);
        await cache.LoadAsync(settings, packages);

        List<PolicyViolation> violations =
            await RunAnalysisStrategies(effectiveProjectPath, settings, packages, getPolicyByProject);

        PackageInfo[] allPackages = packages.GetAllUsedPackages();

        var riskPipeline = new RiskAnalysisPipeline(Logger, getPolicyByProject, riskEvaluator);
        if (settings.ReportRisk || riskPipeline.IsRequiredByPolicy(allPackages))
        {
            if (!settings.ReportRisk)
            {
                Logger.LogInformation(
                    "A policy defines risk-based deny rules, so risk enrichment is running automatically even though --report-risk was not specified.");
            }

            violations.AddRange(await riskPipeline.ExecuteAsync(settings, packages, selectPackagesForRiskScoring));
        }

        await cache.PersistAsync(settings, packages);

        return new AnalysisResult
        {
            Violations = violations.ToArray(),
            Packages = allPackages
        };
    }

    /// <summary>
    /// Runs every <see cref="IProjectAnalysisStrategy"/> (C# and npm) against <paramref name="projectPath"/> and
    /// collects the policy violations they report.
    /// </summary>
    private async Task<List<PolicyViolation>> RunAnalysisStrategies(string projectPath, AnalyzerSettings settings,
        PackageInfoCollection packages, GetPolicyByProject getPolicyByProject)
    {
        IProjectAnalysisStrategy[] strategies =
        [
            new CSharpProjectAnalysisStrategy(getPolicyByProject, licenseFetcher, Logger, OnProjectLockFileLoaded),
            new NpmProjectAnalysisStrategy(getPolicyByProject, Logger)
        ];

        List<PolicyViolation> violations = new();
        foreach (IProjectAnalysisStrategy strategy in strategies)
        {
            violations.AddRange(await strategy.ExecuteAnalysis(projectPath, settings, packages));
        }

        return violations;
    }
}
