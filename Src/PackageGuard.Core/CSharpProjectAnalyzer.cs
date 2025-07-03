using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.ProjectModel;
using PackageGuard.Core.Common;
using Pathy;

namespace PackageGuard.Core;

/// <summary>
/// Analyzes C# projects for compliance with defined policies, such as allowed and denied packages, licenses, and feeds.
/// </summary>
public class CSharpProjectAnalyzer(CSharpProjectScanner scanner, NuGetPackageAnalyzer analyzer)
{
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// A path to a folder containing a solution or project file. If it points to a solution, all the projects in that
    /// solution are included. If it points to a directory with more than one solution, it will fail.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// If specified, a list of packages, versions and licenses that are allowed. Everything else is forbidden.
    /// </summary>
    /// <remarks>
    /// Can be overridden by <see cref="DenyList"/>
    /// </remarks>
    public AllowList AllowList { get; set; } = new();

    /// <summary>
    /// If specified, a list of packages, versions and licenses that are forbidden, even if it was listed in <see cref="AllowList"/>.
    /// </summary>
    public DenyList DenyList { get; set; } = new();

    /// <summary>
    /// One or more NuGet feeds that should be completely ignored during the analysis.
    /// </summary>
    /// <value>
    /// Each feed is wildcard string that can match the NuGet feed name or URL.
    /// </value>
    public string[] IgnoredFeeds { get; set; } = [];

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
    /// Indicates whether analysis results should be cached to improve subsequent execution performance.
    /// When set to true, caching is enabled, and if a cache file is specified and exists, it will be used.
    /// </summary>
    public bool UseCaching { get; set; }

    /// <summary>
    /// Specifies the file path where analysis cache data is stored if <see cref="UseCaching"/> is to <c>true</c>.
    /// </summary>
    public string CacheFilePath { get; set; } = ChainablePath.Current / ".packageguard" / "cache.bin";

    public async Task<PolicyViolation[]> ExecuteAnalysis()
    {
        analyzer.IgnoredFeeds = IgnoredFeeds;

        ValidateConfiguration();

        List<string> projectPaths = scanner.FindProjects(ProjectPath);

        PackageInfoCollection packages = new(Logger);

        if (UseCaching && CacheFilePath.Length > 0)
        {
            Logger.LogInformation("Try loading package cache from {CacheFilePath}", CacheFilePath);
            await packages.TryInitializeFromCache(CacheFilePath);
        }

        await CollectPackagesFrom(projectPaths, packages);

        if (UseCaching)
        {
            await packages.WriteToCache(CacheFilePath);
        }

        return VerifyAgainstPolicy(packages);
    }

    private void ValidateConfiguration()
    {
        if (!AllowList.HasPolicies && !DenyList.HasPolicies)
        {
            throw new ArgumentException("Either a allowlist or a denylist must be specified");
        }
    }

    private async Task CollectPackagesFrom(List<string> projectPaths, PackageInfoCollection packages)
    {
        foreach (ChainablePath projectPath in projectPaths)
        {
            Logger.LogHeader($"Getting metadata for packages in {projectPath}");

            var lockFileLoader = new DotNetLockFileLoader
            {
                Logger = Logger,
                InteractiveRestore = InteractiveRestore,
                ForceRestore = ForceRestore,
                SkipRestore = SkipRestore,
            };

            LockFile? lockFile = lockFileLoader.GetPackageLockFile(projectPath);
            if (lockFile is not null)
            {
                foreach (LockFileLibrary? library in lockFile.Libraries.Where(library => library.Type == "package"))
                {
                    await analyzer.CollectPackageMetadata(projectPath, library.Name, library.Version, packages);
                }
            }
        }
    }

    private PolicyViolation[] VerifyAgainstPolicy(PackageInfoCollection packages)
    {
        var violations = new List<PolicyViolation>();

        foreach (PackageInfo package in packages)
        {
            if (!AllowList.Allows(package) || DenyList.Denies(package))
            {
                violations.Add(new PolicyViolation(package.Name, package.Version, package.License!, package.Projects.ToArray(), package.Source, package.SourceUrl));
            }
        }

        return violations.ToArray();
    }
}
