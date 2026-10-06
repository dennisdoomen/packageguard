using System.ComponentModel;
using JetBrains.Annotations;
using PackageGuard.Core;
using PackageGuard.Core.Scaffolding;
using PackageGuard.Core.Npm;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

/// <summary>
/// Defines the command-line settings for the <c>init</c> command, controlling project discovery, the
/// generated configuration's output location, and how the suggested policy is chosen.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class InitCommandSettings : CommandSettings
{
    [Description(
        "The path to a directory containing a .sln/.slnx file and/or a package.json, a specific .sln/.slnx file, a specific .csproj file, or a specific package.json. Defaults to the current working directory")]
    [CommandArgument(0, "[path]")]
    public string ProjectPath { get; set; } = string.Empty;

    [Description(
        "The path to write the generated configuration file to. Defaults to \".packageguard/config.json\" relative to the solution (or project) directory.")]
    [CommandOption("-c|--config-path|--configPath")]
    public string? ConfigPath { get; set; }

    [Description("Overwrite an existing configuration file instead of refusing to run.")]
    [CommandOption("--overwrite")]
    [DefaultValue(false)]
    public bool Overwrite { get; set; }

    [Description(
        "Skip the interactive question about the kind of software this is, and use the given preset instead: permissive-only, no-network-copyleft, or oss-friendly.")]
    [CommandOption("--preset")]
    public string? Preset { get; set; }

    [Description(
        "Also add risk-based deny rules (risk score, vulnerability severity, deprecation and package age). Without it, you are asked interactively unless --preset is given.")]
    [CommandOption("--risk-gates")]
    [DefaultValue(false)]
    public bool RiskGates { get; set; }

    [Description("Allow enabling or disabling an interactive mode of \"dotnet restore\". Defaults to true")]
    [CommandOption("-i|--restore-interactive|--restoreinteractive")]
    [DefaultValue(true)]
    public bool Interactive { get; set; } = true;

    [Description("Force restoring the NuGet dependencies, even if the lockfile is up-to-date")]
    [CommandOption("-f|--force-restore|--forcerestore")]
    [DefaultValue(false)]
    public bool ForceRestore { get; set; }

    [Description("Prevent the restore operation from running, even if the lock file is missing or out-of-date")]
    [CommandOption("-s|--skip-restore|--skiprestore")]
    [DefaultValue(false)]
    public bool SkipRestore { get; set; }

    [Description(
        "GitHub API key to use for fetching package licenses. If not specified, you may run into GitHub's rate limiting issues.")]
    [CommandOption("-a|--github-api-key|--githubapikey")]
    public string? GitHubApiKey { get; set; } = Environment.GetEnvironmentVariable("GITHUB_API_KEY");

    [Description("Explicitly enable or disable scanning for .csproj, .sln or .slnx files")]
    [CommandOption("--nuget")]
    [DefaultValue(true)]
    public bool ScanNuGet { get; set; } = true;

    [Description(
        "Explicitly specify the package manager to use (npm, yarn, pnpm), or None to disable NPM scanning entirely. If not specified, it will detect it automatically.")]
    [CommandOption("--npm")]
    public NpmPackageManager? NpmPackageManager { get; set; }

    [Description(
        "The directory, package.json or lock file of the npm, yarn or pnpm project, in case it is not in the same directory as the solution or project file.")]
    [CommandOption("--npm-path|--npmpath")]
    public string? NpmProjectPath { get; set; }

    [Description(
        "The path to the npm, yarn or pnpm executable. If not specified, the system PATH is used.")]
    [CommandOption("--npm-exe-path|--npmexepath")]
    public string? NpmExePath { get; set; }

    [Description("Enable verbose (debug-level) logging output.")]
    [CommandOption("-v|--verbose")]
    [DefaultValue(false)]
    public bool Verbose { get; set; }

    /// <summary>
    /// Validates that <see cref="Preset"/>, when specified, is a recognized preset name.
    /// </summary>
    public override ValidationResult Validate()
    {
        if (!string.IsNullOrWhiteSpace(Preset) && !LicensePresets.TryParsePresetName(Preset, out _))
        {
            return ValidationResult.Error(
                $"--preset must be one of {FormatPresetNames()}, but was \"{Preset}\".");
        }

        return ValidationResult.Success();
    }

    private static string FormatPresetNames() =>
        string.Join(", ", LicensePresets.PresetNames.Select(name => $"\"{name}\""));

    /// <summary>
    /// Converts the CLI settings into an <see cref="AnalyzerSettings"/> instance used to scan the repository.
    /// Risk reporting is never enabled, since <c>init</c> only needs license metadata.
    /// </summary>
    public AnalyzerSettings ToCoreSettings()
    {
        return new AnalyzerSettings
        {
            ForceRestore = ForceRestore,
            SkipRestore = SkipRestore,
            InteractiveRestore = Interactive,
            NpmPackageManager = NpmPackageManager,
            NpmExePath = NpmExePath,
            NpmProjectPath = NpmProjectPath,
            ScanNuGet = ScanNuGet,
            ReportRisk = false,
            GitHubApiKey = GitHubApiKey
        };
    }
}
