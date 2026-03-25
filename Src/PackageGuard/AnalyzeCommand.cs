using System.Reflection;
using System.Globalization;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using PackageGuard.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

[UsedImplicitly]
internal sealed class AnalyzeCommand(ILogger logger) : AsyncCommand<AnalyzeCommandSettings>
{
    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 1;

    public override async Task<int> ExecuteAsync(CommandContext context, AnalyzeCommandSettings settings, CancellationToken _)
    {
        // Display PackageGuard version
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        logger.LogHeader($"PackageGuard v{version}");

        var licenseFetcher = new LicenseFetcher(logger, settings.GitHubApiKey);
        var riskEvaluator = new RiskEvaluator(logger);
        var analyzer = new ProjectAnalyzer(licenseFetcher, riskEvaluator)
        {
            Logger = logger,
        };

        var loader = new ConfigurationLoader(logger);

        // Use hierarchical configuration discovery if using default config path and it doesn't exist
        GetPolicyByProject getPolicy = _ => loader.GetConfigurationFromConfigPath(settings.ConfigPath);
        if (settings.ConfigPath == AnalyzeCommandSettings.DefaultConfigFileName && !File.Exists(settings.ConfigPath))
        {
            getPolicy = loader.GetEffectiveConfigurationForProject;
        }

        PolicyViolation[] violations;
        PackageInfo[] packages = [];
        AnalyzerSettings analyzerSettings = settings.ToCoreSettings();

        if (settings.ReportRisk)
        {
            var result = await analyzer.ExecuteAnalysisWithRisk(settings.ProjectPath, analyzerSettings, getPolicy);
            violations = result.Violations;
            packages = result.Packages;
        }
        else
        {
            violations = await analyzer.ExecuteAnalysis(settings.ProjectPath, analyzerSettings, getPolicy);
        }

        logger.LogHeader("Completing analysis");

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

        // Display risk metrics if requested
        if (settings.ReportRisk && packages.Length > 0)
        {
            RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(settings.ProjectPath, packages);

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
        }

        if (violations.Length == 0)
        {
            AnsiConsole.MarkupLine("[green3_1]No policy violations found.[/]");
        }

        return SuccessExitCode;
    }

    private static string GetRiskColor(double score)
    {
        return score switch
        {
            >= 60 => "red1",
            >= 30 => "yellow1",
            _ => "green3_1"
        };
    }

    private static string GetRiskZone(double score)
    {
        return score switch
        {
            >= 60 => "High",
            >= 30 => "Medium",
            _ => "Low"
        };
    }
    private static string FormatDecimal(double value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
