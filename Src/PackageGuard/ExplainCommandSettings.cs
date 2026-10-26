using System.ComponentModel;
using JetBrains.Annotations;
using PackageGuard.Core;
using PackageGuard.Core.Npm;
using Pathy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

/// <summary>
/// Defines the command-line settings for the <c>explain</c> command, controlling which package to explain
/// and the same project discovery, restore, caching, and npm options as the <c>analyze</c> command.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class ExplainCommandSettings : CommandSettings
{
    [Description("The name (or a partial/misspelled name) of the package to explain.")]
    [CommandArgument(0, "<package>")]
    public string PackageName { get; set; } = string.Empty;

    [Description(
        "The exact version to explain, when the package resolved to more than one version across the analyzed projects.")]
    [CommandArgument(1, "[version]")]
    public string? Version { get; set; }

    [Description(
        "The path to a directory containing a .sln/.slnx file and/or a package.json, a specific .sln/.slnx file, a specific .csproj file, or a specific package.json. Defaults to the current working directory")]
    [CommandOption("-p|--path")]
    public string ProjectPath { get; set; } = string.Empty;

    [Description(
        "The path to the configuration file. Defaults to hierarchical discovery of packageguard.config.json or .packageguard/config.json files starting from the solution directory.")]
    [CommandOption("-c|--config-path|--configPath")]
    public string? ConfigPath { get; set; }

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
        "GitHub API key to use for fetching package licenses and risk data. If not specified, you may run into GitHub's rate limiting issues.")]
    [CommandOption("-a|--github-api-key|--githubapikey")]
    public string? GitHubApiKey { get; set; } = Environment.GetEnvironmentVariable("GITHUB_API_KEY");

    [Description(
        "Overrides the file path of the cache that explain reads from, if it exists. Explain never updates the cache. Defaults to the \"<workingdirectory>/.packageguard/cache.bin\"")]
    [CommandOption("--cache-file-path|--cachefilepath")]
    public string CacheFilePath { get; set; } = ChainablePath.Current / ".packageguard" / "cache.bin";

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
    /// Converts the CLI settings into an <see cref="AnalyzerSettings"/> instance used by the core analysis
    /// pipeline. Risk reporting is always enabled, since <c>explain</c> always shows the risk breakdown, and the
    /// cache is always used when it exists but never updated.
    /// </summary>
    public AnalyzerSettings ToCoreSettings()
    {
        return new AnalyzerSettings
        {
            ForceRestore = ForceRestore,
            SkipRestore = SkipRestore,
            InteractiveRestore = Interactive,
            CacheFilePath = CacheFilePath,
            NpmPackageManager = NpmPackageManager,
            UseCaching = true,
            ReadOnlyCache = true,
            NpmExePath = NpmExePath,
            NpmProjectPath = NpmProjectPath,
            ScanNuGet = ScanNuGet,
            ReportRisk = true,
            GitHubApiKey = GitHubApiKey
        };
    }

    /// <summary>
    /// Validates that an explicit <see cref="ConfigPath"/> exists.
    /// </summary>
    public override ValidationResult Validate() => AnalyzeCommandSettings.ValidateConfigPath(ConfigPath);
}
