using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using PackageGuard.Core;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Scaffolding;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;
using Pathy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

/// <summary>
/// CLI command that scans the repository and scaffolds a starter configuration file from the licenses and
/// packages actually found, instead of leaving the user to guess at an allow/deny policy from scratch.
/// </summary>
[UsedImplicitly]
public sealed class InitCommand(ILogger logger) : AsyncCommand<InitCommandSettings>
{
    private const int SuccessExitCode = 0;
    private const int RefusedExitCode = 1;

    protected override async Task<int> ExecuteAsync(CommandContext context, InitCommandSettings settings, CancellationToken _)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        logger.LogHeader($"PackageGuard v{version}");

        if (settings.Verbose)
        {
            logger.LogInformation("Verbose logging enabled — debug-level output is active");
        }

        ChainablePath configPath = ResolveConfigPath(settings);
        if (configPath.IsFile && !settings.Force)
        {
            AnsiConsole.MarkupLine($"[red1]A configuration file already exists at {Markup.Escape(configPath)}.[/]");
            AnsiConsole.MarkupLine("Use --force to overwrite it.");
            return RefusedExitCode;
        }

        string scanPath = string.IsNullOrEmpty(settings.ProjectPath) ? Directory.GetCurrentDirectory() : settings.ProjectPath;
        AnsiConsole.MarkupLine($"Scanning [blue]{Markup.Escape(scanPath)}[/]...");

        PackageInfo[] packages = await ScanAsync(settings);
        if (packages.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow1]No packages were found, so no configuration file was written.[/]");
            return SuccessExitCode;
        }

        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine($"Found {packages.Length} packages across {CountDistinctProjects(packages)} projects.");
        AnsiConsole.MarkupLine("");

        IReadOnlyList<LicenseUsage> usages = LicenseUsageSummarizer.Summarize(packages);
        PrintLicenseUsage(usages);

        SoftwareProfile profile = ResolveProfile(settings);
        IReadOnlyList<string> allowedLicenses = PolicyScaffolder.BuildAllowedLicenses(usages, profile);
        IReadOnlyList<string> warnLicenses = PolicyScaffolder.BuildWarnLicenses(usages, profile);
        bool includeRiskGates = ResolveRiskGates(settings);

        string json = ConfigScaffolder.BuildConfigJson(profile, allowedLicenses, warnLicenses, includeRiskGates);
        WriteConfigFile(configPath, json);

        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine($"Written [blue]{Markup.Escape(configPath)}[/]");

        ReportSuggestedPolicyOutcome(packages, allowedLicenses, warnLicenses, includeRiskGates);

        return SuccessExitCode;
    }

    /// <summary>
    /// Determines where the generated configuration file should be written: an explicit <c>--config-path</c>,
    /// or <c>.packageguard/config.json</c> under the solution directory (falling back to the resolved project
    /// directory when no solution file is found).
    /// </summary>
    private static ChainablePath ResolveConfigPath(InitCommandSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ConfigPath))
        {
            return ChainablePath.From(settings.ConfigPath);
        }

        ChainablePath path = string.IsNullOrEmpty(settings.ProjectPath) ? ChainablePath.Current : settings.ProjectPath;
        if (path.IsFile)
        {
            path = path.Directory;
        }

        ChainablePath solutionDirectory = path.FindParentWithFileMatching("*.sln", "*.slnx");
        ChainablePath baseDirectory = solutionDirectory.IsNull ? path : solutionDirectory;

        return baseDirectory / ".packageguard" / "config.json";
    }

    /// <summary>
    /// Scans the repository using a policy that allows everything, so every package can be collected and
    /// classified without any pre-existing allow/deny decisions getting in the way.
    /// </summary>
    private async Task<PackageInfo[]> ScanAsync(InitCommandSettings settings)
    {
        var licenseFetcher = new LicenseFetcher(logger, settings.GitHubApiKey);
        var analyzer = new ProjectAnalyzer(licenseFetcher) { Logger = logger };

        ProjectPolicy AllowEverything(string _) => new()
        {
            AllowList = new AllowList { Packages = [new PackageSelector("*")] }
        };

        AnalysisResult result = await analyzer.ExecuteAnalysisWithRisk(settings.ProjectPath, settings.ToCoreSettings(), AllowEverything);
        return result.Packages;
    }

    private static int CountDistinctProjects(IEnumerable<PackageInfo> packages) =>
        packages.SelectMany(package => package.Projects).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    /// <summary>
    /// Prints the "Licenses in use" breakdown, flagging copyleft licenses and naming an example package for
    /// licenses that could not be resolved.
    /// </summary>
    private static void PrintLicenseUsage(IReadOnlyList<LicenseUsage> usages)
    {
        AnsiConsole.MarkupLine("Licenses in use:");

        int nameWidth = usages.Max(usage => DisplayName(usage).Length);

        foreach (LicenseUsage usage in usages)
        {
            string name = DisplayName(usage).PadRight(nameWidth);
            string count = usage.PackageCount == 1 ? "1 package" : $"{usage.PackageCount} packages";
            string suffix = DescribeSuffix(usage);

            AnsiConsole.MarkupLine($"  {Markup.Escape(name)}  {Markup.Escape(count)}{Markup.Escape(suffix)}");
        }

        AnsiConsole.MarkupLine("");
    }

    private static string DisplayName(LicenseUsage usage) => usage.License ?? "(unknown)";

    private static string DescribeSuffix(LicenseUsage usage)
    {
        return usage.Category switch
        {
            LicenseCategory.WeakCopyleft => "   <- weak copyleft",
            LicenseCategory.StrongCopyleft => "   <- strong copyleft",
            LicenseCategory.NetworkCopyleft => "   <- network copyleft",
            LicenseCategory.Unknown when usage.ExamplePackages.Count > 0 => $"   <- {usage.ExamplePackages[0]}",
            _ => ""
        };
    }

    /// <summary>
    /// Resolves the <see cref="SoftwareProfile"/> from <c>--preset</c>, or by asking the user interactively
    /// when no preset was given.
    /// </summary>
    private static SoftwareProfile ResolveProfile(InitCommandSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Preset) && LicensePresets.TryParsePresetName(settings.Preset, out SoftwareProfile fromPreset))
        {
            return fromPreset;
        }

        const string proprietary = "Proprietary / commercial   (suggests: permissive only)";
        const string saas = "SaaS / hosted              (suggests: no network copyleft)";
        const string openSource = "Open source                (suggests: OSS friendly)";

        string choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What kind of software is this?")
                .AddChoices(proprietary, saas, openSource));

        return choice switch
        {
            proprietary => SoftwareProfile.Proprietary,
            saas => SoftwareProfile.Saas,
            _ => SoftwareProfile.OpenSource
        };
    }

    /// <summary>
    /// Decides whether to add risk gates: <c>--risk-gates</c> opts in, a preset (non-interactive use) opts out
    /// by default, and otherwise the user is asked, defaulting to no because risk gating makes every run slower.
    /// </summary>
    private static bool ResolveRiskGates(InitCommandSettings settings)
    {
        if (settings.RiskGates || !string.IsNullOrWhiteSpace(settings.Preset))
        {
            return settings.RiskGates;
        }

        return AnsiConsole.Confirm(
            "Also deny packages with a high risk score, serious vulnerabilities, or a very recent release? " +
            "(makes every run slower)",
            defaultValue: false);
    }

    private static void WriteConfigFile(ChainablePath configPath, string json)
    {
        configPath.Directory.CreateDirectoryRecursively();
        File.WriteAllText(configPath, json);
    }

    /// <summary>
    /// Reports how many of the scanned packages would violate or warn under the suggested policy, pointing to
    /// <c>analyze</c> for details and to <c>--treat-deny-as-warning</c> for gradual adoption.
    /// </summary>
    private static void ReportSuggestedPolicyOutcome(PackageInfo[] packages, IReadOnlyList<string> allowedLicenses,
        IReadOnlyList<string> warnLicenses, bool includeRiskGates)
    {
        int violations = PolicyScaffolder.CountViolations(packages, allowedLicenses);
        int warnings = PolicyScaffolder.CountWarnings(packages, allowedLicenses, warnLicenses);

        if (violations == 0)
        {
            AnsiConsole.MarkupLine("[green3_1]No packages violate the suggested policy.[/]");
        }
        else
        {
            string packageWord = violations == 1 ? "package" : "packages";
            AnsiConsole.MarkupLine(
                $"[yellow1]{violations} {packageWord} violate the suggested policy. Run `packageguard .` to see them.[/]");
            AnsiConsole.MarkupLine("To adopt it gradually, add --treat-deny-as-warning so violations don't fail the build yet.");
        }

        if (warnings > 0)
        {
            AnsiConsole.MarkupLine($"{warnings} {(warnings == 1 ? "package is" : "packages are")} allowed but will be reported as warnings (copyleft).");
        }

        if (includeRiskGates)
        {
            AnsiConsole.MarkupLine("Risk gates are only evaluated by `packageguard .`, since they need risk data that `init` doesn't collect.");
        }
    }
}
