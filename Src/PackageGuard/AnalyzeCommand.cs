using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using NuGet.ProjectModel;
using PackageGuard.Core;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;
using PackageGuard.Core.Risk;
using PackageGuard.Core.Sbom;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

/// <summary>
/// CLI command that runs NuGet package analysis against configured allow/deny policies.
/// </summary>
[UsedImplicitly]
public sealed class AnalyzeCommand(ILogger logger) : AsyncCommand<AnalyzeCommandSettings>
{
    /// <summary>
    /// Exit code indicating the analysis completed with no policy violations.
    /// </summary>
    private const int SuccessExitCode = 0;

    /// <summary>
    /// Exit code indicating the analysis found one or more policy violations.
    /// </summary>
    private const int FailureExitCode = 1;

    /// <summary>
    /// Runs the package analysis, reports any policy violations to the console, and writes risk reports when requested.
    /// </summary>
    public override async Task<int> ExecuteAsync(CommandContext context, AnalyzeCommandSettings settings, CancellationToken _)
    {
        // Display PackageGuard version
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        logger.LogHeader($"PackageGuard v{version}");

        if (settings.Verbose)
        {
            logger.LogInformation("Verbose logging enabled — debug-level output is active");
        }

        var analyzer = BuildAnalyzer(logger, settings);

        // The restore graph is already loaded for the analysis, so keeping it for the dependency hygiene section costs nothing extra
        List<LockFile> lockFiles = [];
        if (settings.ReportRisk)
        {
            analyzer.OnProjectLockFileLoaded = (_, lockFile) => lockFiles.Add(lockFile);
        }

        var loader = new ConfigurationLoader(logger, settings.ProjectPath);

        GetPolicyByProject getPolicy = _ => loader.GetConfigurationFromConfigPath(settings.ConfigPath);
        if (settings.ConfigPath == AnalyzeCommandSettings.DefaultConfigFileName && !File.Exists(settings.ConfigPath))
        {
            getPolicy = loader.GetEffectiveConfigurationForProject;
        }

        bool sbomRequested = !string.IsNullOrWhiteSpace(settings.Sbom);
        AnalyzerSettings analyzerSettings = settings.ToCoreSettings();
        (PolicyViolation[] violations, PackageInfo[] packages) =
            await RunAnalysisAsync(analyzer, settings, analyzerSettings, getPolicy, sbomRequested);

        logger.LogHeader("Completing analysis");

        if (sbomRequested && packages.Length > 0)
        {
            WriteSbom(settings, packages, logger);
        }

        // Write risk reports before reporting violations so they are always generated when requested
        RiskReportPaths? reportPaths = null;
        if (settings.ReportRisk && packages.Length > 0)
        {
            reportPaths = await WriteRiskReportsAsync(logger, settings, packages, lockFiles);
        }

        int exitCode = ReportViolations(logger, violations, settings);

        if (reportPaths is not null)
        {
            AnsiConsole.MarkupLine("Detailed risk reports:");
            AnsiConsole.MarkupLine($"HTML: [blue]{Markup.Escape(reportPaths.HtmlPath)}[/]");
            AnsiConsole.MarkupLine($"SARIF: [blue]{Markup.Escape(reportPaths.SarifPath)}[/]");
            AnsiConsole.MarkupLine("");
        }

        return exitCode;
    }

    private static ProjectAnalyzer BuildAnalyzer(ILogger logger, AnalyzeCommandSettings settings)
    {
        var licenseFetcher = new LicenseFetcher(logger, settings.GitHubApiKey);
        var riskEvaluator = new RiskEvaluator(logger);
        var analyzer = new ProjectAnalyzer(licenseFetcher, riskEvaluator) { Logger = logger };

        if (EnrichmentProgressDisplay.IsSupported(settings.Verbose))
        {
            analyzer.OnRiskEnrichmentProgress = new EnrichmentProgressDisplay().Report;
        }

        return analyzer;
    }

    private static async Task<(PolicyViolation[] violations, PackageInfo[] packages)> RunAnalysisAsync(
        ProjectAnalyzer analyzer,
        AnalyzeCommandSettings settings,
        AnalyzerSettings analyzerSettings,
        GetPolicyByProject getPolicy,
        bool sbomRequested)
    {
        if (settings.ReportRisk || sbomRequested)
        {
            var result = await analyzer.ExecuteAnalysisWithRisk(settings.ProjectPath, analyzerSettings, getPolicy);
            return (result.Violations, result.Packages);
        }

        PolicyViolation[] violations = await analyzer.ExecuteAnalysis(settings.ProjectPath, analyzerSettings, getPolicy);
        return (violations, []);
    }

    private static async Task<RiskReportPaths> WriteRiskReportsAsync(ILogger logger, AnalyzeCommandSettings settings, PackageInfo[] packages,
        IReadOnlyCollection<LockFile> lockFiles)
    {
        logger.LogHeader("Writing risk reports");
        logger.LogInformation("Writing detailed HTML and SARIF risk reports for {PackageCount} packages.", packages.Length);

        IReadOnlyCollection<DependencyFinding> dependencyFindings = new DependencyHygieneAnalyzer().Analyze(lockFiles);

        RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(
            settings.ProjectPath, packages, settings.GetReportRiskPath(), dependencyFindings);

        AnsiConsole.MarkupLine("[yellow1]Package Risk Summary:[/]");
        AnsiConsole.MarkupLine("");

        foreach (var package in packages.OrderByDescending(p => p.RiskScore))
        {
            var riskColor = RiskDisplay.GetRiskColor(package.RiskScore);
            AnsiConsole.MarkupLine(
                $"- {Markup.Escape(package.Name)} {Markup.Escape(package.Version)}: [{riskColor}]{RiskDisplay.FormatDecimal(package.RiskScore)}/100 ({RiskDisplay.GetRiskZone(package.RiskScore)})[/]");
        }

        AnsiConsole.MarkupLine("");

        if (dependencyFindings.Count > 0)
        {
            int conflicts = dependencyFindings.Count(f => f.Kind == DependencyFindingKind.VersionConflict);
            AnsiConsole.MarkupLine(
                $"Dependency hygiene: {dependencyFindings.Count - conflicts} redundant references, {conflicts} version conflicts. See the HTML report.");
            AnsiConsole.MarkupLine("");
        }

        return reportPaths;
    }

    private static int ReportViolations(ILogger logger, PolicyViolation[] violations, AnalyzeCommandSettings settings)
    {
        if (violations.Length == 0)
        {
            AnsiConsole.MarkupLine("[green3_1]No policy violations found.[/]");
            return SuccessExitCode;
        }

        // Malicious packages are reported first and apart from the normal violations
        PolicyViolation[] malicious = violations.Where(violation => violation.IsMalicious).ToArray();
        PolicyViolation[] regular = violations.Where(violation => !violation.IsMalicious).ToArray();

        if (malicious.Length > 0)
        {
            AnsiConsole.MarkupLine(
                "[red1]KNOWN MALICIOUS PACKAGES found. Remove them now, and treat every machine that installed them as compromised:[/]");

            AnsiConsole.MarkupLine("");
            WriteViolations(logger, malicious, _ => "MALICIOUS");
        }

        if (regular.Length > 0)
        {
            AnsiConsole.MarkupLine("[red1]Policy violations found:[/]");
            AnsiConsole.MarkupLine("");
            WriteViolations(logger, regular,
                violation => violation.IsWarning || settings.TreatDenyAsWarning ? "WARNING" : "DENIED");
        }

        // A malicious package fails the run even when denies are otherwise treated as warnings
        bool hasFailingViolation = malicious.Length > 0 ||
                                   (regular.Any(violation => !violation.IsWarning) && !settings.TreatDenyAsWarning);

        return hasFailingViolation && !settings.IgnoreViolations ? FailureExitCode : SuccessExitCode;
    }

    private static void WriteViolations(ILogger logger, PolicyViolation[] violations, Func<PolicyViolation, string> getLabel)
    {
        foreach (var violation in violations)
        {
            logger.LogInformation("{Id} {Version} [{Label}]", violation.PackageId, violation.Version, getLabel(violation));
            logger.LogInformation("- License: {License}", violation.License);
            logger.LogInformation("- Feed: {Source} ({Url})", violation.FeedName, violation.FeedUrl);

            if (!string.IsNullOrWhiteSpace(violation.Reason))
            {
                logger.LogInformation("- Reason: {Reason}", violation.Reason);
            }

            if (violation.Projects.Any())
            {
                logger.LogInformation("- Projects:");
            }

            foreach (string project in violation.Projects)
            {
                logger.LogInformation("  - {Project}", project);
            }

            AnsiConsole.MarkupLine("");
        }
    }

    /// <summary>
    /// Writes the SBOM for <paramref name="packages"/> in the requested format
    /// (CycloneDX or SPDX) to <see cref="AnalyzeCommandSettings.SbomOutput"/>.
    /// </summary>
    private static void WriteSbom(AnalyzeCommandSettings settings, PackageInfo[] packages, ILogger logger)
    {
        logger.LogHeader("Writing SBOM");

        SbomWriter.Write(packages, settings.ProjectPath, settings.Sbom!, settings.SbomOutput!);

        AnsiConsole.MarkupLine($"SBOM ({settings.Sbom!.ToLowerInvariant()}): [blue]{Markup.Escape(settings.SbomOutput!)}[/]");
        AnsiConsole.MarkupLine("");
    }

}
