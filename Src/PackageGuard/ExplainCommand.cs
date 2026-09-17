using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using NuGet.ProjectModel;
using PackageGuard.Core;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;
using PackageGuard.Core.Risk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

/// <summary>
/// CLI command that explains why a specific package is present in the analyzed project(s): its dependency
/// path, how its version was resolved, the policy rule (and configuration file) that allowed or denied it,
/// and its risk breakdown.
/// </summary>
[UsedImplicitly]
public sealed class ExplainCommand(ILogger logger) : AsyncCommand<ExplainCommandSettings>
{
    private const int SuccessExitCode = 0;
    private const int NotFoundExitCode = 1;

    protected override async Task<int> ExecuteAsync(CommandContext context, ExplainCommandSettings settings, CancellationToken _)
    {
        if (settings.Verbose)
        {
            logger.LogInformation("Verbose logging enabled — debug-level output is active");
        }

        var licenseFetcher = new LicenseFetcher(logger, settings.GitHubApiKey);
        var analyzer = new ProjectAnalyzer(licenseFetcher, new RiskEvaluator(logger)) { Logger = logger };

        var loader = new ConfigurationLoader(logger);
        GetPolicyByProject getPolicy = _ => loader.GetConfigurationFromConfigPath(settings.ConfigPath);
        if (settings.ConfigPath == AnalyzeCommandSettings.DefaultConfigFileName && !File.Exists(settings.ConfigPath))
        {
            getPolicy = loader.GetEffectiveConfigurationForProject;
        }

        List<(string ProjectPath, LockFile LockFile)> lockFilesByProject = new();
        analyzer.OnProjectLockFileLoaded = (projectPath, lockFile) => lockFilesByProject.Add((projectPath, lockFile));

        logger.LogHeader($"Analyzing to explain \"{settings.PackageName}\"");

        // Resolving the target inside the risk-scoring selector, rather than after the analysis completes,
        // means only the resolved package (and its own dependency closure) ever has its risk signals fetched -
        // not every package used across the whole solution.
        ExplainTarget? target = null;
        AnalysisResult result = await analyzer.ExecuteAnalysisWithRisk(settings.ProjectPath, settings.ToCoreSettings(), getPolicy,
            allPackages =>
            {
                target = ResolveTarget(settings, allPackages);
                return target.Package is not null ? [target.Package] : [];
            });

        if (target is null || target.MatchedName is null)
        {
            return ReportNoMatch(settings.PackageName, target?.Suggestions ?? []);
        }

        if (target.Package is null)
        {
            if (target.AmbiguousVersions.Length > 0)
            {
                ReportAmbiguousVersion(target.MatchedName, target.AmbiguousVersions);
                return SuccessExitCode;
            }

            AnsiConsole.MarkupLine(
                $"[red1]{Markup.Escape(target.MatchedName)} {Markup.Escape(settings.Version ?? "")} was not found. Available versions: {Markup.Escape(string.Join(", ", target.AvailableVersions))}[/]");
            return NotFoundExitCode;
        }

        ExplainPackage(target.Package, getPolicy, lockFilesByProject);

        return SuccessExitCode;
    }

    /// <summary>
    /// Resolves the package the caller asked to explain against every package used in this run: by name
    /// (exact, partial, or fuzzy), then by version when more than one was found.
    /// </summary>
    private static ExplainTarget ResolveTarget(ExplainCommandSettings settings, PackageInfo[] allPackages)
    {
        PackageNameMatch match = PackageNameMatcher.Resolve(settings.PackageName, allPackages.Select(p => p.Name).ToArray());
        if (match.MatchedName is null)
        {
            return new ExplainTarget(null, null, [], [], match.Suggestions);
        }

        PackageInfo[] versions = allPackages
            .Where(p => p.Name.Equals(match.MatchedName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        string[] distinctVersions = versions.Select(p => p.Version).Distinct().ToArray();

        if (!string.IsNullOrWhiteSpace(settings.Version))
        {
            PackageInfo? exact = versions.FirstOrDefault(p => p.Version.Equals(settings.Version, StringComparison.OrdinalIgnoreCase));
            return new ExplainTarget(exact, match.MatchedName, [], distinctVersions, []);
        }

        if (distinctVersions.Length > 1)
        {
            return new ExplainTarget(null, match.MatchedName, versions, distinctVersions, []);
        }

        return new ExplainTarget(versions[0], match.MatchedName, [], distinctVersions, []);
    }

    /// <summary>
    /// The outcome of resolving an <c>explain</c> target: either a single package to explain, or enough
    /// information to report why one couldn't be pinned down (no match, an unknown version, or an ambiguous
    /// name that resolved to more than one version).
    /// </summary>
    private sealed record ExplainTarget(
        PackageInfo? Package,
        string? MatchedName,
        PackageInfo[] AmbiguousVersions,
        string[] AvailableVersions,
        IReadOnlyList<string> Suggestions);

    /// <summary>
    /// Prints "no exact match" guidance, including any partial-name or edit-distance suggestions found.
    /// </summary>
    private static int ReportNoMatch(string query, IReadOnlyList<string> suggestions)
    {
        AnsiConsole.MarkupLine($"[red1]No package matching \"{Markup.Escape(query)}\" was found.[/]");

        if (suggestions.Count > 0)
        {
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("Did you mean:");
            foreach (string suggestion in suggestions)
            {
                AnsiConsole.MarkupLine($"  - {Markup.Escape(suggestion)}");
            }
        }

        return NotFoundExitCode;
    }

    /// <summary>
    /// Prints every resolved version of a package when the caller didn't specify which one to explain.
    /// </summary>
    private static void ReportAmbiguousVersion(string name, IReadOnlyCollection<PackageInfo> versions)
    {
        AnsiConsole.MarkupLine(
            $"[yellow1]{Markup.Escape(name)} resolved to {versions.Select(p => p.Version).Distinct().Count()} different versions. Re-run with a version to pick one:[/]");
        AnsiConsole.MarkupLine("");

        foreach (PackageInfo version in versions.OrderBy(p => p.Version))
        {
            AnsiConsole.MarkupLine(
                $"  - {Markup.Escape(version.Version)}: {Markup.Escape(version.License ?? "Unknown")} license, from {Markup.Escape(version.Source)}");
        }
    }

    /// <summary>
    /// Renders the full explanation for a single resolved package version: identity, policy verdict(s),
    /// dependency path(s), and risk breakdown.
    /// </summary>
    private void ExplainPackage(PackageInfo package, GetPolicyByProject getPolicy,
        IReadOnlyCollection<(string ProjectPath, LockFile LockFile)> lockFilesByProject)
    {
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(package.Name)} {Markup.Escape(package.Version)}[/]");
        string licenseSuffix = package.License is not null ? $" ({DescribeLicenseEvidence(package.LicenseEvidence)})" : "";
        AnsiConsole.MarkupLine($"  License:  {Markup.Escape(package.License ?? "Unknown")}{licenseSuffix}");
        AnsiConsole.MarkupLine($"  Feed:     {Markup.Escape(package.Source)} ({Markup.Escape(package.SourceUrl)})");

        ExplainVerdict(package, getPolicy);

        AnsiConsole.MarkupLine("");
        if (package.GetPackageEcosystem().Equals("nuget", StringComparison.OrdinalIgnoreCase))
        {
            ExplainNuGetDependencyPaths(package, lockFilesByProject);
        }
        else
        {
            AnsiConsole.MarkupLine(
                "[grey]Dependency path resolution is not yet available for npm/yarn/pnpm packages.[/]");
            PrintReferencingProjects(package);
        }

        ExplainRisk(package);
    }

    /// <summary>
    /// Prints the allow/deny verdict for every project referencing the package, naming the rule (and, when
    /// known, the configuration file) that decided it.
    /// </summary>
    private static void ExplainVerdict(PackageInfo package, GetPolicyByProject getPolicy)
    {
        string[] projects = package.Projects.Length > 0 ? package.Projects : [""];
        bool showPerProject = projects.Length > 1;

        foreach (string project in projects)
        {
            ProjectPolicy policy = getPolicy(project);

            PolicyDecision allowDecision = policy.AllowList.EvaluateAllow(package);
            PolicyDecision denyDecision = policy.DenyList.EvaluateDeny(package);
            bool isViolation = !allowDecision.IsMatch || denyDecision.IsMatch;

            string status = isViolation ? "DENIED" : "ALLOWED";
            string statusColor = isViolation ? "red1" : "green3_1";
            PolicyDecision decisive = denyDecision.IsMatch ? denyDecision : allowDecision;

            string reason = decisive.Reason;
            if (decisive.SourceFile is not null)
            {
                reason += $" (from {decisive.SourceFile})";
            }

            string label = showPerProject ? $"Status ({Path.GetFileNameWithoutExtension(project)}):" : "Status:  ";
            AnsiConsole.MarkupLine($"  {label} [{statusColor}]{status}[/] ({Markup.Escape(reason)})");
        }
    }

    /// <summary>
    /// Resolves and prints the dependency chain(s) that pull the package into each referencing NuGet
    /// project, along with the version-resolution detail for the requester closest to the target.
    /// </summary>
    private static void ExplainNuGetDependencyPaths(PackageInfo package,
        IReadOnlyCollection<(string ProjectPath, LockFile LockFile)> lockFilesByProject)
    {
        var chainsByProject = lockFilesByProject
            .Where(entry => package.Projects.Contains(entry.ProjectPath, StringComparer.OrdinalIgnoreCase))
            .Select(entry => (Project: entry.ProjectPath,
                Chains: NuGetDependencyPathFinder.FindChains(entry.LockFile, package.Name, package.Version)))
            .Where(entry => entry.Chains.Count > 0)
            .ToArray();

        if (chainsByProject.Length == 0)
        {
            PrintReferencingProjects(package);
            return;
        }

        AnsiConsole.MarkupLine("Required by:");
        foreach (var (project, chains) in chainsByProject)
        {
            AnsiConsole.MarkupLine($"  {Markup.Escape(Path.GetFileNameWithoutExtension(project))}");
            foreach (DependencyChain chain in chains)
            {
                PrintChain(chain);
            }
        }

        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("Version resolution:");
        foreach (var (project, chains) in chainsByProject)
        {
            foreach (DependencyChain chain in chains)
            {
                string requester = chain.Hops.Count > 1 ? chain.Hops[^2].Name : Path.GetFileNameWithoutExtension(project);
                string range = chain.Hops[^1].RequestedRange ?? "(any)";
                AnsiConsole.MarkupLine($"    {Markup.Escape(requester)} requested {Markup.Escape(range)} -> resolved to {Markup.Escape(package.Version)}");
            }
        }
    }

    /// <summary>
    /// Prints a single dependency chain, indenting one level deeper per hop, marking the final hop as
    /// direct when the chain has only one hop.
    /// </summary>
    private static void PrintChain(DependencyChain chain)
    {
        for (int i = 0; i < chain.Hops.Count; i++)
        {
            DependencyHop hop = chain.Hops[i];
            string indent = new string(' ', 4 + i * 3);
            string suffix = i == 0 && chain.Hops.Count == 1 ? "   (direct)" : "";
            AnsiConsole.MarkupLine($"{indent}-> {Markup.Escape(hop.Name)} {Markup.Escape(hop.Version)}{suffix}");
        }
    }

    private static void PrintReferencingProjects(PackageInfo package)
    {
        if (package.Projects.Length == 0)
        {
            return;
        }

        AnsiConsole.MarkupLine("Referenced by:");
        foreach (string project in package.Projects)
        {
            AnsiConsole.MarkupLine($"  - {Markup.Escape(project)}");
        }
    }

    /// <summary>
    /// Prints the per-factor risk breakdown already computed by <see cref="RiskEvaluator"/>, mirroring the
    /// wording used by the HTML risk report.
    /// </summary>
    private static void ExplainRisk(PackageInfo package)
    {
        string riskColor = RiskDisplay.GetRiskColor(package.RiskScore);
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine(
            $"Risk: [{riskColor}]{RiskDisplay.FormatDecimal(package.RiskScore)}/100 ({RiskDisplay.GetRiskZone(package.RiskScore)})[/]");

        PrintRiskDimension("Legal", package.RiskDimensions.LegalRisk, package.RiskDimensions.LegalRiskRationale);
        PrintRiskDimension("Security", package.RiskDimensions.SecurityRisk, package.RiskDimensions.SecurityRiskRationale);
        PrintRiskDimension("Operational", package.RiskDimensions.OperationalRisk, package.RiskDimensions.OperationalRiskRationale);
    }

    private static void PrintRiskDimension(string name, double score, string[] rationale)
    {
        string details = rationale.Length > 0 ? string.Join("; ", rationale) : "no signals available";
        AnsiConsole.MarkupLine($"  {name,-12} {RiskDisplay.FormatDecimal(score)}   {Markup.Escape(details)}");
    }

    private static string DescribeLicenseEvidence(LicenseEvidence evidence)
    {
        return evidence switch
        {
            LicenseEvidence.Declared => "from package metadata",
            LicenseEvidence.Concluded => "concluded from external evidence",
            _ => "unknown provenance"
        };
    }
}
