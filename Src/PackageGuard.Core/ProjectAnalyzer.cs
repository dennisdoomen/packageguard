using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.ProjectModel;
using PackageGuard.Core.Common;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Npm;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;
using PackageGuard.Core.Risk;
using PackageGuard.Core.Risk.Enrichment;

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

        IProjectAnalysisStrategy[] strategies =
        [
            new CSharpProjectAnalysisStrategy(getPolicyByProject, licenseFetcher, Logger, OnProjectLockFileLoaded),
            new NpmProjectAnalysisStrategy(getPolicyByProject, Logger)
        ];

        List<PolicyViolation> violations = new();

        PackageInfoCollection packages = new(Logger, settings);
        bool isCachingEnabled = settings is { UseCaching: true, CacheFilePath.Length: > 0 };
        if (isCachingEnabled)
        {
            Logger.LogInformation("Try loading package cache from {CacheFilePath}", settings.CacheFilePath);
            await packages.TryInitializeFromCache(settings.CacheFilePath);
            await GitHubApi.LoadCachesAsync(Logger, settings.CacheFilePath, settings);
        }

        foreach (IProjectAnalysisStrategy strategy in strategies)
        {
            violations.AddRange(await strategy.ExecuteAnalysis(effectiveProjectPath, settings, packages));
        }

        PackageInfo[] allPackages = packages.GetAllUsedPackages();

        bool anyPolicyNeedsRisk = allPackages
            .SelectMany(package => package.Projects)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(usedProjectPath => getPolicyByProject(usedProjectPath))
            .Any(policy => policy.DenyList.HasRiskPolicies);

        if (settings.ReportRisk || anyPolicyNeedsRisk)
        {
            if (!settings.ReportRisk)
            {
                Logger.LogInformation(
                    "A policy defines risk-based deny rules, so risk enrichment is running automatically even though --report-risk was not specified.");
            }

            await BuildRiskReport(settings, packages, selectPackagesForRiskScoring);
            violations.AddRange(EvaluateRiskBasedViolations(allPackages, getPolicyByProject));
        }

        if (settings.UseCaching)
        {
            await packages.WriteToCache(settings.CacheFilePath);
        }

        if (isCachingEnabled)
        {
            await GitHubApi.SaveCachesAsync(Logger, settings.CacheFilePath);
        }

        return new AnalysisResult
        {
            Violations = violations.ToArray(),
            Packages = allPackages
        };
    }

    private async Task BuildRiskReport(AnalyzerSettings settings, PackageInfoCollection packages,
        Func<PackageInfo[], PackageInfo[]>? selectPackagesForRiskScoring)
    {
        PackageInfo[] allPackages = packages.GetAllUsedPackages();
        PackageInfo[] scoringTargets = selectPackagesForRiskScoring?.Invoke(allPackages) ?? allPackages;

        if (scoringTargets.Length == 0)
        {
            return;
        }

        // Every risk factor considers only the scoring targets and, for the transitive-dependency factors,
        // their own dependency closure - not necessarily every package used across the whole solution.
        PackageInfo[] enrichmentSet = selectPackagesForRiskScoring is null
            ? allPackages
            : CollectTransitiveClosure(scoringTargets, allPackages);

        Logger.LogHeader("Collecting risk metadata");

        Logger.LogInformation(
            "Building risk report data for {PackageCount} packages. This can take a while while repository, release, and security signals are refreshed.",
            enrichmentSet.Length);

        var enricher = new ParallelPackageRiskEnricher(Logger, settings.GitHubApiKey);
        await enricher.EnrichAsync(enrichmentSet);

        IReadOnlyDictionary<string, PackageInfo> packagesByKey = packages.CreatePackagesByKey();
        var transitiveVulnEnricher = new TransitiveVulnerabilityCountEnricher(packagesByKey);
        var healthEnricher = new DependencyHealthCountEnricher(packagesByKey);

        foreach (PackageInfo package in scoringTargets)
        {
            await transitiveVulnEnricher.EnrichAsync(package);
            await healthEnricher.EnrichAsync(package);
        }

        Logger.LogInformation("Risk metadata collection complete. Calculating package risk scores.");

        RiskEvaluator evaluator = riskEvaluator ?? new RiskEvaluator(Logger);
        foreach (PackageInfo package in scoringTargets)
        {
            evaluator.EvaluateRisk(package);
        }

        Logger.LogInformation("Risk scoring complete for {PackageCount} packages.", scoringTargets.Length);
    }

    /// <summary>
    /// Checks every package against the risk-based deny rules of the policy of each project that references
    /// it, skipping packages covered by a currently-applicable <see cref="RiskException"/>. Requires
    /// <paramref name="packages"/> to have already been scored by the risk pipeline.
    /// </summary>
    private static PolicyViolation[] EvaluateRiskBasedViolations(PackageInfo[] packages, GetPolicyByProject getPolicyByProject)
    {
        List<PolicyViolation> violations = new();

        foreach (PackageInfo package in packages)
        {
            foreach (string projectPath in package.Projects)
            {
                ProjectPolicy policy = getPolicyByProject(projectPath);
                if (!policy.DenyList.HasRiskPolicies || policy.IsExcludedFromRiskDenial(package))
                {
                    continue;
                }

                PolicyDecision decision = policy.DenyList.EvaluateRiskDeny(package);
                if (decision.IsMatch)
                {
                    violations.Add(new PolicyViolation(package.Name, package.Version, package.License ?? "",
                        package.Projects.ToArray(), package.Source, package.SourceUrl, decision.Reason));
                    break;
                }
            }
        }

        return violations.ToArray();
    }

    /// <summary>
    /// Collects <paramref name="roots"/> plus every package reachable from them through
    /// <see cref="PackageInfo.DependencyKeys"/>, so risk signals can be fetched for exactly the packages a
    /// transitive-dependency risk factor needs and nothing else.
    /// </summary>
    private static PackageInfo[] CollectTransitiveClosure(IReadOnlyCollection<PackageInfo> roots,
        IReadOnlyCollection<PackageInfo> allPackages)
    {
        IReadOnlyDictionary<string, PackageInfo> packagesByKey = allPackages
            .GroupBy(package => package.CreatePackageKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        List<PackageInfo> closure = new();
        Queue<PackageInfo> queue = new();

        foreach (PackageInfo root in roots)
        {
            if (visited.Add(root.CreatePackageKey()))
            {
                closure.Add(root);
                queue.Enqueue(root);
            }
        }

        while (queue.Count > 0)
        {
            PackageInfo current = queue.Dequeue();
            foreach (string dependencyKey in current.DependencyKeys)
            {
                if (visited.Add(dependencyKey) && packagesByKey.TryGetValue(dependencyKey, out PackageInfo? dependency))
                {
                    closure.Add(dependency);
                    queue.Enqueue(dependency);
                }
            }
        }

        return closure.ToArray();
    }
}
