using Microsoft.Extensions.Logging;
using PackageGuard.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

internal sealed class AnalyzeCommand(ILogger logger) : AsyncCommand<AnalyzeCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AnalyzeCommandSettings settings)
    {
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


        ConfigurationLoader.Configure(analyzer, settings.ConfigPath);

        var violations = await analyzer.ExecuteAnalysis();

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
}
