using System.Reflection;
using Microsoft.Extensions.Logging;
using PackageGuard.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

internal sealed class AnalyzeCommand(ILogger logger) : AsyncCommand<AnalyzeCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AnalyzeCommandSettings settings)
    {
        // Display PackageGuard version
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        logger.LogHeader($"PackageGuard v{version}");

        var projectScanner = new CSharpProjectScanner(logger)
        {
            SelectSolution = solutions =>
            {
                string selected = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a solution :")
                        .AddChoices(solutions));

                return selected;
            }
        };

        var licenseFetcher = new LicenseFetcher(logger, settings.GitHubApiKey);
        var analyzer = new CSharpProjectAnalyzer(projectScanner, new NuGetPackageAnalyzer(logger, licenseFetcher))
        {
            ProjectPath = settings.ProjectPath,
            InteractiveRestore = settings.Interactive,
            ForceRestore = settings.ForceRestore,
            SkipRestore = settings.SkipRestore,
            UseCaching = settings.UseCaching,
            CacheFilePath = settings.CacheFilePath,
            Logger = logger,
        };

        PolicyViolation[] violations;

        // Use hierarchical configuration discovery if using default config path and it doesn't exist
        if (settings.ConfigPath == AnalyzeCommandSettings.DefaultConfigFileName && !File.Exists(settings.ConfigPath))
        {
            violations = await ExecuteHierarchicalAnalysis(analyzer);
        }
        else
        {
            ConfigurationLoader.ConfigureForBackwardsCompatibility(analyzer, settings.ConfigPath);
            violations = await analyzer.ExecuteAnalysis();
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

            return 1;
        }

        AnsiConsole.MarkupLine("[green3_1]No policy violations found.[/]");

        return 0;
    }

    private async Task<PolicyViolation[]> ExecuteHierarchicalAnalysis(CSharpProjectAnalyzer analyzer)
    {
        var projectScanner = new CSharpProjectScanner(logger);
        List<string> projectPaths = projectScanner.FindProjects(analyzer.ProjectPath);

        var allViolations = new List<PolicyViolation>();

        // Analyze each project with its own effective configuration
        foreach (string projectPath in projectPaths)
        {
            logger.LogHeader($"Analyzing project with hierarchical configuration: {projectPath}");

            // Create a new analyzer instance for this project with its specific configuration
            var projectAnalyzer = new CSharpProjectAnalyzer(projectScanner, new NuGetPackageAnalyzer(logger, new LicenseFetcher(logger, "")))
            {
                ProjectPath = projectPath,
                InteractiveRestore = analyzer.InteractiveRestore,
                ForceRestore = analyzer.ForceRestore,
                SkipRestore = analyzer.SkipRestore,
                UseCaching = analyzer.UseCaching,
                CacheFilePath = analyzer.CacheFilePath,
                Logger = logger,
            };

            // Configure this analyzer with the effective configuration for this specific project
            ConfigurationLoader.ConfigureHierarchical(projectAnalyzer, projectPath);

            // Execute analysis for this specific project
            allViolations.AddRange(await projectAnalyzer.ExecuteAnalysis());
        }

        return allViolations.ToArray();
    }
}
