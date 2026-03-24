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
                AnsiConsole.MarkupLine($"- Overall Risk: [{riskColor}]{FormatDecimal(package.RiskScore)}/100[/]");
                WriteRiskDimension("Legal", package.RiskDimensions.LegalRisk, package.RiskDimensions.LegalRiskRationale);
                WriteRiskDimension("Security", package.RiskDimensions.SecurityRisk, package.RiskDimensions.SecurityRiskRationale);
                WriteRiskDimension("Operational", package.RiskDimensions.OperationalRisk, package.RiskDimensions.OperationalRiskRationale);
                logger.LogInformation("- License: {License}", package.License ?? "Unknown");

                if (package.HasValidLicenseUrl is not null)
                {
                    logger.LogInformation("- License URL: {Status}", package.HasValidLicenseUrl == true ? "Valid" : "Missing or invalid");
                }

                if (package.IsPackageSigned is not null)
                {
                    logger.LogInformation("- Package signature: {Status}", package.IsPackageSigned == true ? "Signed" : "Unsigned");
                }

                if (package.HasTrustedPackageSignature is not null)
                {
                    logger.LogInformation("- Signature trust: {Status}",
                        package.HasTrustedPackageSignature == true ? "Verified" : "Unverified");
                }

                if (package.VulnerabilityCount > 0)
                {
                    logger.LogInformation("- Vulnerabilities: {Count} (max severity {Severity})",
                        package.VulnerabilityCount, FormatDecimal(package.MaxVulnerabilitySeverity));
                }

                if (package.DependencyDepth > 0)
                {
                    logger.LogInformation("- Dependency depth: {Depth}", package.DependencyDepth);
                }

                if (!string.IsNullOrWhiteSpace(package.LatestStableVersion))
                {
                    logger.LogInformation("- Latest stable version: {LatestStableVersion}", package.LatestStableVersion);
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

                if (package.TopContributorShare is double topContributorShare)
                {
                    logger.LogInformation("- Top contributor share: {Share}",
                        topContributorShare.ToString("P0", CultureInfo.InvariantCulture));
                }

                if (package.RecentFailedWorkflowCount is int recentFailedWorkflowCount)
                {
                    logger.LogInformation("- Recent failed workflows: {RecentFailedWorkflowCount}", recentFailedWorkflowCount);
                }

                if (package.OpenSsfScore is double openSsfScore)
                {
                    logger.LogInformation("- OpenSSF Scorecard: {OpenSsfScore}", FormatDecimal(openSsfScore));
                }

                if (package.SupportedTargetFrameworks.Length > 0)
                {
                    logger.LogInformation("- Target frameworks: {SupportedTargetFrameworks}",
                        string.Join(", ", package.SupportedTargetFrameworks));
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

    private static void WriteRiskDimension(string label, double score, string[] rationale)
    {
        AnsiConsole.MarkupLine($"- {label}: [{GetRiskColor(score * 10)}]{FormatDecimal(score)}/10[/]");

        foreach (string reason in rationale)
        {
            AnsiConsole.MarkupLine($"  - {Markup.Escape(reason)}");
        }
    }

    private static string FormatDecimal(double value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
