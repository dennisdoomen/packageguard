using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PackageGuard.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

internal sealed class AnalyzeCommand(ILogger logger) : AsyncCommand<AnalyzeCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AnalyzeCommandSettings settings)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(settings.ConfigPath, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var globalSettings = configuration.GetSection("Settings").Get<GlobalSettings>() ?? new GlobalSettings();

        var projectScanner = new ProjectScanner(logger)
        {
            SelectSolution = solutions =>
            {
                string selected = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a solution :")
                        .AddChoices<string>(solutions));

                return selected;
            }
        };

        var analyzer = new NuGetProjectAnalyzer(projectScanner, new NuGetPackageAnalyzer(logger))
        {
            ProjectPath = settings.ProjectPath, Logger = logger,
        };

        Configure(analyzer, globalSettings);

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

    private void Configure(NuGetProjectAnalyzer analyzer, GlobalSettings globalSettings)
    {
        foreach (string package in globalSettings.AllowList.Packages)
        {
            string[] segments = package.Split("/");

            analyzer.AllowList.Packages.Add(new PackageSelector(segments[0], segments.ElementAtOrDefault(1) ?? ""));
        }

        analyzer.AllowList.Licenses.AddRange(globalSettings.AllowList.Licenses);

        foreach (string package in globalSettings.DenyList.Packages)
        {
            string[] segments = package.Split("/");

            analyzer.DenyList.Packages.Add(new PackageSelector(segments[0], segments.ElementAtOrDefault(1) ?? ""));
        }

        analyzer.DenyList.Licenses.AddRange(globalSettings.DenyList.Licenses);
    }
}
