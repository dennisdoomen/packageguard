using Pathy;

namespace PackageGuard.Core;

public class AnalyzerSettings
{
    /// <summary>
    /// Specifies whether interactive mode should be enabled for the .NET restore process.
    /// When enabled, the restore operation may prompt for user input, such as authentication information.
    /// </summary>
    /// <value>
    /// Defaults to <c>true</c>.
    /// </value>
    public bool InteractiveRestore { get; set; } = true;

    /// <summary>
    /// Force restoring the NuGet dependencies, even if the lockfile is up-to-date
    /// </summary>
    public bool ForceRestore { get; set; }

    /// <summary>
    /// Determines whether to skip the restore operation for the project analysis.
    /// If set to true, no project or solution restore will be performed before analyzing the project dependencies.
    /// </summary>
    public bool SkipRestore { get; set; }

    /// <summary>
    /// Indicates whether analysis results should be cached to improve the performance of further analysis.
    /// When set to true, caching is enabled, and if a cache file is specified and exists, it will be used.
    /// </summary>
    public bool UseCaching { get; set; }

    /// <summary>
    /// Specifies the file path where analysis cache data is stored if <see cref="UseCaching"/> is to <c>true</c>.
    /// </summary>
    public string CacheFilePath { get; set; } = ChainablePath.Current / ".packageguard" / "cache.bin";

    public bool ScanNuGet { get; set; }

    public string? NpmExePatch { get; set; }

    public NpmPackageManager? NpmPackageManager { get; set; }
}
