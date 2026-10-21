using Microsoft.Extensions.Logging;
using PackageGuard.Core.Common;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;
using PackageGuard.Core.Risk.Enrichment;

namespace PackageGuard.Core.Risk;

/// <summary>
/// Runs risk enrichment and scoring for a single analysis run, and evaluates the resulting
/// risk-based policy violations. Construct a fresh instance per run.
/// </summary>
internal sealed class RiskAnalysisPipeline(ILogger logger, GetPolicyByProject getPolicyByProject,
    RiskEvaluator? riskEvaluator = null, Action<int, int>? onEnrichmentProgress = null)
{
    /// <summary>
    /// Determines whether any policy applicable to <paramref name="allPackages"/> defines risk-based
    /// deny rules, meaning risk enrichment must run even when <c>--report-risk</c> was not requested.
    /// </summary>
    public bool IsRequiredByPolicy(PackageInfo[] allPackages) =>
        allPackages
            .SelectMany(package => package.Projects)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(usedProjectPath => getPolicyByProject(usedProjectPath))
            .Any(policy => policy.DenyList.HasRiskPolicies);

    /// <summary>
    /// Enriches and scores the relevant packages in <paramref name="packages"/>, then evaluates the
    /// risk-based deny rules, returning any resulting violations.
    /// </summary>
    public async Task<PolicyViolation[]> ExecuteAsync(AnalyzerSettings settings, PackageInfoCollection packages,
        Func<PackageInfo[], PackageInfo[]>? selectPackagesForRiskScoring = null)
    {
        PackageInfo[] allPackages = packages.GetAllUsedPackages();
        await BuildRiskReport(settings, packages, allPackages, selectPackagesForRiskScoring);
        return EvaluateRiskBasedViolations(allPackages);
    }

    private async Task BuildRiskReport(AnalyzerSettings settings, PackageInfoCollection packages,
        PackageInfo[] allPackages, Func<PackageInfo[], PackageInfo[]>? selectPackagesForRiskScoring)
    {
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

        logger.LogHeader("Collecting risk metadata");

        logger.LogInformation(
            "Building risk report data for {PackageCount} packages. This can take a while while repository, release, and security signals are refreshed.",
            enrichmentSet.Length);

        var enricher = new ParallelPackageRiskEnricher(logger, settings.GitHubApiKey);
        await enricher.EnrichAsync(enrichmentSet, logger, onEnrichmentProgress);

        // Needs the OSV vulnerabilities, so it cannot be one of the parallel enrichers
        await new ExploitDataEnricher(logger).EnrichAsync(enrichmentSet);
        ApplyVulnerabilityExceptions(enrichmentSet);

        IReadOnlyDictionary<string, PackageInfo> packagesByKey = packages.CreatePackagesByKey();
        var transitiveVulnEnricher = new TransitiveVulnerabilityCountEnricher(packagesByKey);
        var healthEnricher = new DependencyHealthCountEnricher(packagesByKey);

        foreach (PackageInfo package in scoringTargets)
        {
            await transitiveVulnEnricher.EnrichAsync(package);
            await healthEnricher.EnrichAsync(package);
        }

        logger.LogInformation("Risk metadata collection complete. Calculating package risk scores.");

        RiskEvaluator evaluator = riskEvaluator ?? new RiskEvaluator(logger);
        foreach (PackageInfo package in scoringTargets)
        {
            evaluator.EvaluateRisk(package);
        }

        logger.LogInformation("Risk scoring complete for {PackageCount} packages.", scoringTargets.Length);
    }

    /// <summary>
    /// Marks the vulnerabilities that a policy exception covers and recomputes the vulnerability count and highest
    /// severity from the ones that still count. Done on every run, so removing an exception takes effect right away
    /// even for packages whose OSV data came from the cache.
    /// </summary>
    internal void ApplyVulnerabilityExceptions(IEnumerable<PackageInfo> packages)
    {
        foreach (PackageInfo package in packages.Where(package => package.Vulnerabilities.Length > 0))
        {
            foreach (OsvVulnerabilityRecord vulnerability in package.Vulnerabilities)
            {
                vulnerability.Exception = FindExceptionInEveryProject(package, vulnerability);
            }

            OsvVulnerabilityRecord[] active = package.ActiveVulnerabilities.ToArray();
            package.VulnerabilityCount = active.Length;
            package.MaxVulnerabilitySeverity = active.Length == 0 ? 0 : active.Max(vulnerability => vulnerability.Severity);
        }
    }

    /// <summary>
    /// Returns the exception for a vulnerability only when the policy of every project that uses the package has one;
    /// when they differ, the one that has to be reviewed first applies.
    /// </summary>
    private VulnerabilityException? FindExceptionInEveryProject(PackageInfo package, OsvVulnerabilityRecord vulnerability)
    {
        VulnerabilityException?[] exceptions = package.Projects
            .Select(projectPath => getPolicyByProject(projectPath).FindVulnerabilityException(package, vulnerability))
            .ToArray();

        return exceptions.Length == 0 || exceptions.Any(exception => exception is null)
            ? null
            : exceptions.OrderBy(exception => exception!.ReviewOn).First();
    }

    /// <summary>
    /// Checks every package against the risk-based deny rules of the policy of each project that references
    /// it, skipping packages covered by a currently-applicable <see cref="RiskException"/>. Requires
    /// <paramref name="packages"/> to have already been scored by the risk pipeline.
    /// </summary>
    internal PolicyViolation[] EvaluateRiskBasedViolations(PackageInfo[] packages)
    {
        List<PolicyViolation> violations = new();

        foreach (PackageInfo package in packages)
        {
            // A malicious package is blocked regardless of policy, risk score or risk exceptions
            if (package.MaliciousAdvisoryIds is { Length: > 0 } advisoryIds)
            {
                violations.Add(new PolicyViolation
                {
                    PackageId = package.Name,
                    Version = package.Version,
                    License = package.License ?? "",
                    Projects = package.Projects.ToArray(),
                    FeedName = package.Source,
                    FeedUrl = package.SourceUrl,
                    Reason = $"known malicious package ({string.Join(", ", advisoryIds)})",
                    IsMalicious = true,
                    Resolution = "Remove the package and every package that depends on it"
                });

                continue;
            }

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
                    violations.Add(PolicyViolation.FromDecision(package, package.Projects.ToArray(), decision));
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
