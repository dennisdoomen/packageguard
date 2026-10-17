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
public sealed class DependenciesCommand(ILogger logger) : AsyncCommand<DependenciesCommandSettings>
{
    private const int SuccessExitCode = 0;
    private const int FindingsExitCode = 1;

    public override Task<int> ExecuteAsync(CommandContext context, DependenciesCommandSettings settings, CancellationToken _)
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

        bool fail = settings.Severity == DependencySeverity.Error && findings.Length > 0;
        return Task.FromResult(fail ? FindingsExitCode : SuccessExitCode);
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
            AnsiConsole.MarkupLine($"[bold]Redundant package references ({redundancies.Length})[/]");
            foreach (IGrouping<string, DependencyFinding> project in redundancies.GroupBy(f => f.Project).OrderBy(g => g.Key))
            {
                ReportProject(project);
            }

            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("  [green3_1]✓[/] safe to remove   [yellow1]![/] changes the resolved version");
            AnsiConsole.MarkupLine(
                "[grey]  Based on the restore graph, not your source code. A package can be redundant here and still be used directly in code.[/]");
        }

        if (conflicts.Length > 0)
        {
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine($"[bold]Version conflicts ({conflicts.Length})[/]");
            foreach (DependencyFinding finding in conflicts)
            {
                ReportConflict(finding);
            }
        }
    }

    private static void ReportProject(IGrouping<string, DependencyFinding> project)
    {
        // The same finding in several target frameworks is printed once, with all frameworks listed.
        var rows = project
            .GroupBy(f => (f.PackageId, Providers: string.Join(", ", f.Providers), f.SafeToRemove, f.ResolvedVersion, f.WouldResolveTo))
            .Select(g => (g.Key, Frameworks: string.Join(", ", g.Select(f => f.TargetFramework).Distinct().Order())))
            .OrderBy(r => r.Key.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string frameworks = string.Join(", ", rows.SelectMany(r => r.Frameworks.Split(", ")).Distinct().Order());
        AnsiConsole.MarkupLine($"  [bold]{Markup.Escape(project.Key)}[/] [grey]({Markup.Escape(frameworks)})[/]");

        // A grid keeps multi-line provider lists aligned under their own column.
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadLeft(4).PadRight(1));
        grid.AddColumn(new GridColumn().NoWrap().PadLeft(0).PadRight(2));
        grid.AddColumn(new GridColumn().PadLeft(0).PadRight(0));

        bool mixedFrameworks = rows.Any(r => r.Frameworks != frameworks);
        foreach (var row in rows)
        {
            string marker = row.Key.SafeToRemove ? "[green3_1]✓[/]" : "[yellow1]![/]";
            string note = row.Key.SafeToRemove ? "" : $"  [yellow1]({row.Key.ResolvedVersion} → {row.Key.WouldResolveTo})[/]";
            string onlyIn = mixedFrameworks ? $" [grey]({Markup.Escape(row.Frameworks)})[/]" : "";

            // One provider per line, so a long list never wraps in the middle of a name.
            string providers = string.Join('\n', row.Key.Providers.Split(", ").Select((p, i) =>
                $"{(i == 0 ? "←" : " ")} {Markup.Escape(p)}"));

            grid.AddRow(new Markup(marker), new Markup(Markup.Escape(row.Key.PackageId)),
                new Markup($"{providers}{note}{onlyIn}"));
        }

        AnsiConsole.Write(grid);
    }

    private static void ReportConflict(DependencyFinding finding)
    {
        // Description is "<package> resolves to multiple versions: <v> (<projects>); <v> (<projects>)."
        const string separator = " resolves to multiple versions: ";
        int index = finding.Description.IndexOf(separator, StringComparison.Ordinal);
        if (index < 0)
        {
            AnsiConsole.MarkupLine($"  {Markup.Escape(finding.Description)}");
            return;
        }

        AnsiConsole.MarkupLine($"  [bold]{Markup.Escape(finding.PackageId)}[/]");
        foreach (string version in finding.Description[(index + separator.Length)..].TrimEnd('.').Split("; "))
        {
            int open = version.IndexOf(" (", StringComparison.Ordinal);
            string number = open < 0 ? version : version[..open];
            string projects = open < 0 ? "" : version[(open + 2)..^1];
            AnsiConsole.MarkupLine($"    {Markup.Escape(number.PadRight(10))}[grey]{Markup.Escape(projects)}[/]");
        }
    }
}
