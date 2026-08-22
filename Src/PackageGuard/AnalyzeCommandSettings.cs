using System.ComponentModel;
using JetBrains.Annotations;
using PackageGuard.Core;
using PackageGuard.Core.Npm;
using Pathy;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard;

/// <summary>
/// Defines the command-line settings for the <c>analyze</c> command, controlling project paths, restore behavior,
/// caching, npm scanning, and risk reporting output.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class AnalyzeCommandSettings : CommandSettings
{
    /// <summary>
    /// The default name of the PackageGuard configuration file.
    /// </summary>
    /// <summary>
    /// The default name of the PackageGuard configuration file.
    /// </summary>
    public const string DefaultConfigFileName = "config.json";

    /// <summary>
    /// Environment variable that overrides the output path for the risk report, used in CI pipelines.
    /// </summary>
    internal const string ReportRiskPathOverrideEnvironmentVariable = "PACKAGEGUARD_REPORT_RISK_PATH_OVERRIDE";

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

    [Description("Force --report-risk to rebuild risk-related package data instead of reusing cached risk entries.")]
    [CommandOption("--refresh-risk-cache|--refreshriskcache")]
    [DefaultValue(false)]
    public bool RefreshRiskCache { get; set; }

    [Description("Maximum age in hours for cached risk-related package data before --report-risk refreshes it.")]
    [CommandOption("--risk-cache-max-age-hours|--riskcachemaxagehours")]
    [DefaultValue(24)]
    public int RiskCacheMaxAgeHours { get; set; } = 24;

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

    [Description(
        "Show a colored risk summary in the console and generate detailed HTML/SARIF risk reports. Optionally provide a directory or file path. Directories receive generated file names; explicit filenames are used directly and may overwrite prior files.")]
    [CommandOption("--report-risk|--reportrisk")]
    public bool ReportRisk { get; set; }

    [Description("Enable verbose (debug-level) logging output. Combine with --report-risk to see individual HTTP calls to GitHub, OSV, and npm registries.")]
    [CommandOption("-v|--verbose")]
    [DefaultValue(false)]
    public bool Verbose { get; set; }

    [Description(
        "Generate a Software Bill of Materials for the resolved dependency graph, in the given format: cyclonedx or spdx. Requires --sbom-output.")]
    [CommandOption("--sbom")]
    public string? Sbom { get; set; }

    [Description("The output file path for the generated SBOM. Required when --sbom is specified.")]
    [CommandOption("--sbom-output|--sbomoutput")]
    public string? SbomOutput { get; set; }

    /// <summary>
    /// Validates that <see cref="Sbom"/>, when specified, is a supported format and is paired with <see cref="SbomOutput"/>.
    /// </summary>
    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Sbom))
        {
            return ValidationResult.Success();
        }

        if (!Sbom.Equals("cyclonedx", StringComparison.OrdinalIgnoreCase) &&
            !Sbom.Equals("spdx", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Error($"--sbom must be either \"cyclonedx\" or \"spdx\", but was \"{Sbom}\".");
        }

        if (string.IsNullOrWhiteSpace(SbomOutput))
        {
            return ValidationResult.Error("--sbom-output must be specified when --sbom is used.");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Returns the report risk output path when overridden via the
    /// <see cref="ReportRiskPathOverrideEnvironmentVariable"/> environment variable, or <c>null</c> if not set.
    /// </summary>
    public string? GetReportRiskPath()
    {
        string? reportRiskPath = Environment.GetEnvironmentVariable(ReportRiskPathOverrideEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(reportRiskPath))
        {
            return null;
        }

        return reportRiskPath;
    }

    /// <summary>
    /// Converts the CLI settings into an <see cref="AnalyzerSettings"/> instance used by the core analysis pipeline.
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
            UseCaching = UseCaching,
            NpmExePath = NpmExePath,
            ScanNuGet = ScanNuGet,
            ReportRisk = ReportRisk,
            GitHubApiKey = GitHubApiKey,
            RefreshRiskCache = RefreshRiskCache,
            RiskCacheMaxAge = TimeSpan.FromHours(Math.Max(0, RiskCacheMaxAgeHours))
        };
    }
}
