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

    public async Task<PolicyViolation[]> ExecuteAnalysis(GetPolicyByProject getPolicyByProject)
    {
        List<PolicyViolation> violations = new();
        analyzer.InteractiveRestore = InteractiveRestore;

        PackageInfoCollection packages = new(Logger);
        if (UseCaching && CacheFilePath.Length > 0)
        {
            Logger.LogInformation("Try loading package cache from {CacheFilePath}", CacheFilePath);
            await packages.TryInitializeFromCache(CacheFilePath);
        }

        List<string> projectPaths = scanner.FindProjects(ProjectPath);
        foreach (string projectPath in projectPaths)
        {
            packages.Clear();

            ProjectPolicy policy = getPolicyByProject(projectPath);
            policy.Validate();

            analyzer.IgnoredFeeds = policy.IgnoredFeeds;

            await CollectPackagesFrom(projectPath, packages);

            violations.AddRange(VerifyAgainstPolicy(packages, policy));
        }

        if (UseCaching)
        {
            await packages.WriteToCache(CacheFilePath);
        }

        return violations.ToArray();
    }

    private async Task CollectPackagesFrom(string projectPath, PackageInfoCollection packages)
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

    private PolicyViolation[] VerifyAgainstPolicy(PackageInfoCollection packages, ProjectPolicy policy)
    {
        var violations = new List<PolicyViolation>();

        foreach (PackageInfo package in packages)
        {
            if (!policy.AllowList.Allows(package) || policy.DenyList.Denies(package))
            {
                violations.Add(new PolicyViolation(package.Name, package.Version, package.License!, package.Projects.ToArray(), package.Source, package.SourceUrl));
            }
        }

        return violations.ToArray();
    }
}
