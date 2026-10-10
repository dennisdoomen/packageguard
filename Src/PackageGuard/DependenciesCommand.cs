using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.CSharp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

/// <summary>
/// CLI command that finds redundant package references and version conflicts in the restored dependency graph of
/// NuGet projects. It works offline: no package metadata is fetched and no policy is evaluated.
/// </summary>
[UsedImplicitly]
public sealed class DependenciesCommand(ILogger logger) : Command<DependenciesCommandSettings>
{
    private const int SuccessExitCode = 0;
    private const int FindingsExitCode = 1;

    public override int Execute(CommandContext context, DependenciesCommandSettings settings, CancellationToken _)
    {
        if (settings.Verbose)
        {
            logger.LogInformation("Verbose logging enabled — debug-level output is active");
        }

        var collector = new LockFileCollector
        {
            Logger = logger,
            InteractiveRestore = settings.Interactive,
            ForceRestore = settings.ForceRestore,
            SkipRestore = settings.SkipRestore
        };

        var analyzer = new DependencyHygieneAnalyzer
        {
            ExcludedPackageIds = settings.ExcludedPackageIds
        };

        DependencyFinding[] findings = analyzer
            .Analyze(collector.Collect(settings.ProjectPath))
            .Where(settings.IsReported)
            .ToArray();

        Report(findings);

        return settings.Severity == DependencySeverity.Error && findings.Length > 0 ? FindingsExitCode : SuccessExitCode;
    }

    private static void Report(DependencyFinding[] findings)
    {
        if (findings.Length == 0)
        {
            AnsiConsole.MarkupLine("[green3_1]No redundant package references or version conflicts found.[/]");
            return;
        }

        DependencyFinding[] redundancies = findings.Where(f => f.Kind != DependencyFindingKind.VersionConflict).ToArray();
        DependencyFinding[] conflicts = findings.Where(f => f.Kind == DependencyFindingKind.VersionConflict).ToArray();

        if (redundancies.Length > 0)
        {
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[bold]Redundant package references[/]");
            foreach (DependencyFinding finding in redundancies)
            {
                string safety = finding.SafeToRemove ? "[green3_1]safe to remove[/]" : "[yellow1]changes the resolved version[/]";
                AnsiConsole.MarkupLine(
                    $"  {Markup.Escape(finding.Project)} ({Markup.Escape(finding.TargetFramework)}): {Markup.Escape(finding.Description)} ({safety})");
            }

            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine(
                "[grey]This is based on the restore graph, not your source code. A package can be redundant here and still be used directly in code.[/]");
        }

        if (conflicts.Length > 0)
        {
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[bold]Version conflicts[/]");
            foreach (DependencyFinding finding in conflicts)
            {
                AnsiConsole.MarkupLine($"  {Markup.Escape(finding.Description)}");
            }
        }
    }
}
