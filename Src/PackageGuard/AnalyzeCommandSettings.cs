using System.ComponentModel;
using JetBrains.Annotations;
using PackageGuard.Core;
using Pathy;
using Spectre.Console.Cli;

namespace PackageGuard;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
internal class AnalyzeCommandSettings : CommandSettings
{
    public const string DefaultConfigFileName = "config.json";

    [Description(
        "The path to a directory containing a .sln/.slnx file and/or a package.json, a specific .sln/.slnx file, a specific .csproj file, or a specific package.json. Defaults to the current working directory")]
    [CommandArgument(0, "[path]")]
    public string ProjectPath { get; set; } = string.Empty;

    [Description(
        "The path to the configuration file. Defaults to hierarchical discovery of packageguard.config.json or .packageguard/config.json files starting from the solution directory.")]
    [CommandOption("-c|--config-path|--configPath")]
    public string ConfigPath { get; set; } = DefaultConfigFileName;

    [Description("Allow enabling or disabling an interactive mode of \"dotnet restore\". Defaults to true")]
    [CommandOption("-i|--restore-interactive|--restoreinteractive")]
    [DefaultValue(true)]
    public bool Interactive { get; set; } = true;

    [Description("Don't fail the analysis if any violations are found. Defaults to false.")]
    [CommandOption("--ignore-violations|--ignoreviolations|--ignore")]
    [DefaultValue(false)]
    public bool IgnoreViolations { get; set; }

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

    [Description("Maintains a cache of the package information to speed up future analysis.")]
    [CommandOption("--use-caching|--usecaching")]
    [DefaultValue(false)]
    public bool UseCaching { get; set; }

    [Description(
        "Overrides the file path where analysis data is cached. Defaults to the \"<workingdirectory>/.packageguard/cache.bin\"")]
    [CommandOption("--cache-file-path|--cachefilepath")]
    public string CacheFilePath { get; set; } = ChainablePath.Current / ".packageguard" / "cache.bin";

    [Description("Explicitly enable or disable scanning for .csproj, .sln or .slnx files")]
    [CommandOption("--nuget")]
    [DefaultValue(true)]
    public bool ScanNuGet { get; set; }

    [Description(
        "Explicitly specify the package manager to use (npm, yarn, pnpm). If not specified, it will detect it automatically.")]
    [CommandOption("--npm")]
    public NpmPackageManager? NpmPackageManager { get; set; }

    [Description(
        "The path to the npm, yarn or pnpm executable. If not specified, the system PATH is used.")]
    [CommandOption("--npm-exe-path|--npmexepath")]
    public string? NpmExePath { get; set; }

    public AnalyzerSettings ToCoreSettings()
    {
        return new AnalyzerSettings
        {
            ForceRestore = ForceRestore,
            SkipRestore = SkipRestore,
            InteractiveRestore = Interactive,
            CacheFilePath = CacheFilePath,
            NpmPackageManager = NpmPackageManager,
            UseCaching = UseCaching,
            NpmExePath = NpmExePath,
            ScanNuGet = ScanNuGet
        };
    }
}
