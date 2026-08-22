using System.Globalization;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
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
public sealed class AnalyzeCommand : AsyncCommand<AnalyzeCommandSettings>
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
    protected override async Task<int> ExecuteAsync(CommandContext context, AnalyzeCommandSettings settings, CancellationToken _)
    {
        if (context.Data is not ILogger logger)
        {
            throw new InvalidOperationException("The command logger was not provided.");
        }

        // Display PackageGuard version
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        logger.LogHeader($"PackageGuard v{version}");

        if (settings.Verbose)
        {
            logger.LogInformation("Verbose logging enabled — debug-level output is active");
        }

        var analyzer = BuildAnalyzer(logger, settings);
        var loader = new ConfigurationLoader(logger);

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
        if (settings.ReportRisk && packages.Length > 0)
        {
            await WriteRiskReportsAsync(logger, settings, packages);
        }

        return ReportViolations(logger, violations, settings);
    }

    private static ProjectAnalyzer BuildAnalyzer(ILogger logger, AnalyzeCommandSettings settings)
    {
        var licenseFetcher = new LicenseFetcher(logger, settings.GitHubApiKey);
        var riskEvaluator = new RiskEvaluator(logger);
        return new ProjectAnalyzer(licenseFetcher, riskEvaluator) { Logger = logger };
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

    private static async Task WriteRiskReportsAsync(ILogger logger, AnalyzeCommandSettings settings, PackageInfo[] packages)
    {
        logger.LogHeader("Writing risk reports");
        logger.LogInformation("Writing detailed HTML and SARIF risk reports for {PackageCount} packages.", packages.Length);

        RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(
            settings.ProjectPath, packages, settings.GetReportRiskPath());

        AnsiConsole.MarkupLine("[yellow1]Package Risk Summary:[/]");
        AnsiConsole.MarkupLine("");

        foreach (var package in packages.OrderByDescending(p => p.RiskScore))
        {
            var riskColor = GetRiskColor(package.RiskScore);
            AnsiConsole.MarkupLine(
                $"- {Markup.Escape(package.Name)} {Markup.Escape(package.Version)}: [{riskColor}]{FormatDecimal(package.RiskScore)}/100 ({GetRiskZone(package.RiskScore)})[/]");
        }

        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("Detailed risk reports:");
        AnsiConsole.MarkupLine($"HTML: [blue]{Markup.Escape(reportPaths.HtmlPath)}[/]");
        AnsiConsole.MarkupLine($"SARIF: [blue]{Markup.Escape(reportPaths.SarifPath)}[/]");
        AnsiConsole.MarkupLine("");
    }

    private static int ReportViolations(ILogger logger, PolicyViolation[] violations, AnalyzeCommandSettings settings)
    {
        if (violations.Length > 0)
        {
            AnsiConsole.MarkupLine("[red1]Policy violations found:[/]");
            AnsiConsole.MarkupLine("");

            foreach (var violation in violations)
            {
                logger.LogInformation("{Id} {Version}", violation.PackageId, violation.Version);
                logger.LogInformation("- License: {License}", violation.License);
                logger.LogInformation("- Feed: {Source} ({Url})", violation.FeedName, violation.FeedUrl);

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

            return settings.IgnoreViolations ? SuccessExitCode : FailureExitCode;
        }

        AnsiConsole.MarkupLine("[green3_1]No policy violations found.[/]");
        return SuccessExitCode;
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

    /// <summary>
    /// Maps a 0–100 risk score to an Ansi console color name for display.
    /// </summary>
    private static string GetRiskColor(double score)
    {
        return score switch
        {
            >= 60 => "red1",
            >= 30 => "yellow1",
            _ => "green3_1"
        };
    }

    /// <summary>
    /// Maps a 0–100 risk score to a risk zone label: Low, Medium, or High.
    /// </summary>
    private static string GetRiskZone(double score)
    {
        return score switch
        {
            >= 60 => "High",
            >= 30 => "Medium",
            _ => "Low"
        };
    }

    /// <summary>
    /// Formats a double value to one decimal place using invariant culture.
    /// </summary>
    private static string FormatDecimal(double value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
