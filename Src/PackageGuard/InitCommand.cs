using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using PackageGuard.Core;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Init;
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
        IReadOnlyList<string> allowedLicenses = InitPolicyBuilder.BuildAllowedLicenses(usages, profile);

        WriteConfigFile(configPath, profile, allowedLicenses);

        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine($"Written [blue]{Markup.Escape(configPath)}[/]");

        ReportSuggestedPolicyOutcome(packages, allowedLicenses);

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

    private static void WriteConfigFile(ChainablePath configPath, SoftwareProfile profile, IReadOnlyList<string> allowedLicenses)
    {
        configPath.Directory.CreateDirectoryRecursively();
        File.WriteAllText(configPath, InitConfigWriter.BuildConfigJson(profile, allowedLicenses));
    }

    /// <summary>
    /// Reports how many of the scanned packages would violate the suggested policy, pointing to <c>analyze</c>
    /// for details when at least one does.
    /// </summary>
    private static void ReportSuggestedPolicyOutcome(PackageInfo[] packages, IReadOnlyList<string> allowedLicenses)
    {
        int violations = InitPolicyBuilder.CountViolations(packages, allowedLicenses);
        if (violations == 0)
        {
            AnsiConsole.MarkupLine("[green3_1]No packages violate the suggested policy.[/]");
            return;
        }

        string packageWord = violations == 1 ? "package" : "packages";
        AnsiConsole.MarkupLine(
            $"[yellow1]{violations} {packageWord} violate the suggested policy. Run `packageguard .` to see them.[/]");
    }
}
