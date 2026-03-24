using System.Reflection;
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
        PackageInfo[] packages = Array.Empty<PackageInfo>();
        AnalyzerSettings analyzerSettings = settings.ToCoreSettings();

        if (settings.ShowRisk)
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
        if (settings.ShowRisk && packages.Length > 0)
        {
            AnsiConsole.MarkupLine("[yellow1]Package Risk Analysis:[/]");
            AnsiConsole.MarkupLine("");

            foreach (var package in packages.OrderByDescending(p => p.RiskScore))
            {
                var riskColor = GetRiskColor(package.RiskScore);
                logger.LogInformation("{Id} {Version}", package.Name, package.Version);
                AnsiConsole.MarkupLine($"- Overall Risk: [{riskColor}]{package.RiskScore:F1}/100[/]");
                AnsiConsole.MarkupLine($"- Legal: [{GetRiskColor(package.RiskDimensions.LegalRisk * 10)}]{package.RiskDimensions.LegalRisk:F1}/10[/]");
                AnsiConsole.MarkupLine($"- Security: [{GetRiskColor(package.RiskDimensions.SecurityRisk * 10)}]{package.RiskDimensions.SecurityRisk:F1}/10[/]");
                AnsiConsole.MarkupLine($"- Operational: [{GetRiskColor(package.RiskDimensions.OperationalRisk * 10)}]{package.RiskDimensions.OperationalRisk:F1}/10[/]");
                logger.LogInformation("- License: {License}", package.License ?? "Unknown");

                if (package.HasValidLicenseUrl is not null)
                {
                    logger.LogInformation("- License URL: {Status}", package.HasValidLicenseUrl == true ? "Valid" : "Missing or invalid");
                }

                if (package.VulnerabilityCount > 0)
                {
                    logger.LogInformation("- Vulnerabilities: {Count} (max severity {Severity:F1})",
                        package.VulnerabilityCount, package.MaxVulnerabilitySeverity);
                }

                if (package.DependencyDepth > 0)
                {
                    logger.LogInformation("- Dependency depth: {Depth}", package.DependencyDepth);
                }

                if (!string.IsNullOrEmpty(package.RepositoryUrl))
                {
                    logger.LogInformation("- Repository: {RepositoryUrl}", package.RepositoryUrl);
                }

                if (package.PublishedAt is DateTimeOffset publishedAt)
                {
                    logger.LogInformation("- Published: {PublishedAt:yyyy-MM-dd}", publishedAt);
                }

                if (package.DownloadCount is long downloadCount)
                {
                    logger.LogInformation("- Downloads: {DownloadCount}", downloadCount);
                }

                if (package.ContributorCount is int contributorCount)
                {
                    logger.LogInformation("- Contributors: {ContributorCount}", contributorCount);
                }

                if (package.OpenBugIssueCount is int openBugIssueCount)
                {
                    logger.LogInformation("- Open bug issues: {OpenBugIssueCount}", openBugIssueCount);
                }

                AnsiConsole.MarkupLine("");
            }
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
            >= 70 => "red1",        // High risk
            >= 40 => "orange1",     // Medium risk
            >= 20 => "yellow1",     // Low-Medium risk
            _ => "green3_1"         // Low risk
        };
    }
}
