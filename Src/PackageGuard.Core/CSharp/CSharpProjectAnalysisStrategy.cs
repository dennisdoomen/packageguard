using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.ProjectModel;
using PackageGuard.Core.Common;

namespace PackageGuard.Core.CSharp;

/// <summary>
/// Analyzes C# projects for compliance with defined policies, such as allowed and denied packages, licenses, and feeds.
/// </summary>
public class CSharpProjectAnalysisStrategy(GetPolicyByProject getPolicyByProject, LicenseFetcher licenseFetcher, ILogger? logger)
    : IProjectAnalysisStrategy
{
    private readonly ILogger logger = logger ?? NullLogger.Instance;

    /// <summary>
    /// Analyzes C# projects found at <paramref name="projectOrSolutionPath"/> for NuGet policy violations,
    /// collecting package metadata and checking each package against the configured allow/deny lists and license policies.
    /// </summary>
    public async Task<PolicyViolation[]> ExecuteAnalysis(string projectOrSolutionPath, AnalyzerSettings settings,
        PackageInfoCollection packages)
    {
        List<PolicyViolation> violations = new();

        if (settings.ScanNuGet)
        {
            var analyzer = new NuGetPackageAnalyzer(logger, licenseFetcher);
            var scanner = new CSharpProjectScanner(logger);

            analyzer.InteractiveRestore = settings.InteractiveRestore;

            List<string> projectPaths = scanner.FindProjects(projectOrSolutionPath);
            foreach (string projectPath in projectPaths)
            {
                packages.Clear();

                ProjectPolicy policy = getPolicyByProject(projectPath);
                policy.Validate();

                analyzer.IgnoredFeeds = policy.IgnoredFeeds;

                await CollectPackagesFrom(projectPath, settings, packages, analyzer);

                violations.AddRange(VerifyAgainstPolicy(packages, policy));
            }
        }
        else
        {
            logger.LogInformation("NuGet scanning was disabled, so skipping .NET package analysis");
        }

        return violations.ToArray();
    }

    /// <summary>
    /// Restores and loads the lock file for <paramref name="projectPath"/>, then collects metadata for every
    /// package library it references and stamps each with its dependency depth, dependency keys, and pre-1.0 flag.
    /// </summary>
    private async Task CollectPackagesFrom(string projectPath, AnalyzerSettings settings, PackageInfoCollection packages,
        NuGetPackageAnalyzer analyzer)
    {
        logger.LogHeader($"Getting metadata for packages in {projectPath}");

        var lockFileLoader = new DotNetLockFileLoader
        {
            Logger = logger,
            InteractiveRestore = settings.InteractiveRestore,
            ForceRestore = settings.ForceRestore,
            SkipRestore = settings.SkipRestore,
        };

        LockFile? lockFile = lockFileLoader.GetPackageLockFile(projectPath);
        if (lockFile is not null)
        {
            var dependencyDepths = CalculateDependencyDepths(lockFile);
            var dependencyKeys = BuildDependencyKeys(lockFile);
            var preOneZeroDependencies = FindPackagesDependingOnPreOneZeroPackages(lockFile);

            foreach (LockFileLibrary? library in lockFile.Libraries.Where(library => library.Type == "package"))
            {
                await analyzer.CollectPackageMetadata(projectPath, library.Name, library.Version, packages);
            }

            foreach (PackageInfo package in packages)
            {
                string key = package.CreatePackageKey();

                if (dependencyDepths.TryGetValue(key, out int depth))
                {
                    package.DependencyDepth = Math.Max(package.DependencyDepth, depth);
                }

                if (dependencyKeys.TryGetValue(key, out string[]? dependencies))
                {
                    package.DependencyKeys = dependencies;
                }

                if (preOneZeroDependencies.Contains(key))
                {
                    package.HasPreOneZeroDependencies = true;
                }
            }
        }
    }

    /// <summary>
    /// Checks every package in <paramref name="packages"/> against the allow/deny lists and license rules in
    /// <paramref name="policy"/> and returns a violation record for each non-compliant package.
    /// </summary>
    private PolicyViolation[] VerifyAgainstPolicy(PackageInfoCollection packages, ProjectPolicy policy)
    {
        var violations = new List<PolicyViolation>();

        foreach (PackageInfo package in packages)
        {
            UpdateLicensePolicyCompatibility(package, policy);

            if (!policy.AllowList.Allows(package) || policy.DenyList.Denies(package))
            {
                violations.Add(new PolicyViolation(package.Name, package.Version, package.License!, package.Projects.ToArray(),
                    package.Source, package.SourceUrl));
            }
        }

        return violations.ToArray();
    }

    /// <summary>
    /// Updates <see cref="PackageInfo.IsLicensePolicyCompatible"/> by checking the package license against
    /// the policy allow/deny license lists, ANDing the result with any previously recorded compatibility value.
    /// </summary>
    private static void UpdateLicensePolicyCompatibility(PackageInfo package, ProjectPolicy policy)
    {
        bool allowlistCompatible = !policy.AllowList.Licenses.Any() ||
                                   policy.AllowList.Licenses.Contains(package.License!, StringComparer.OrdinalIgnoreCase);

        bool denylistCompatible = !policy.DenyList.Licenses.Contains(package.License!, StringComparer.OrdinalIgnoreCase);

        bool compatible = allowlistCompatible && denylistCompatible;
        package.IsLicensePolicyCompatible ??= true;
        package.IsLicensePolicyCompatible &= compatible;
    }

    /// <summary>
    /// Performs a BFS over the lock-file dependency graph and returns a dictionary mapping each package key
    /// to its shallowest depth from a direct project dependency (depth 1 = direct dependency).
    /// </summary>
    private static Dictionary<string, int> CalculateDependencyDepths(LockFile lockFile)
    {
        Dictionary<string, int> depths = new(StringComparer.OrdinalIgnoreCase);
        LockFileTarget? target = lockFile.Targets.FirstOrDefault();
        if (target is null)
        {
            return depths;
        }

        Dictionary<string, LockFileTargetLibrary> libraries = target.Libraries
            .Where(library => !string.IsNullOrWhiteSpace(library.Name) && library.Version is not null)
            .ToDictionary(library =>
                {
                    string name = library.Name!;
                    string version = library.Version!.ToNormalizedString();
                    return PackageInfo.CreatePackageKey(name, version);
                },
                StringComparer.OrdinalIgnoreCase);

        Queue<(string Name, string Version, int Depth)> queue = new();

        foreach (string dependency in lockFile.PackageSpec.TargetFrameworks
                     .SelectMany(tf => tf.Dependencies)
                     .Select(d => d.Name)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            LockFileTargetLibrary? library = target.Libraries.FirstOrDefault(l =>
                string.Equals(l.Name, dependency, StringComparison.OrdinalIgnoreCase));

            if (library is not null && library.Version is not null && !string.IsNullOrWhiteSpace(library.Name))
            {
                queue.Enqueue((library.Name!, library.Version.ToNormalizedString(), 1));
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            string key = PackageInfo.CreatePackageKey(current.Name, current.Version);

            if (depths.TryGetValue(key, out int existingDepth) && existingDepth <= current.Depth)
            {
                continue;
            }

            depths[key] = current.Depth;

            if (!libraries.TryGetValue(key, out LockFileTargetLibrary? library))
            {
                continue;
            }

            foreach (var dependency in library.Dependencies)
            {
                LockFileTargetLibrary? child = target.Libraries.FirstOrDefault(l =>
                    string.Equals(l.Name, dependency.Id, StringComparison.OrdinalIgnoreCase));

                if (child is not null && child.Version is not null && !string.IsNullOrWhiteSpace(child.Name))
                {
                    queue.Enqueue((child.Name!, child.Version.ToNormalizedString(), current.Depth + 1));
                }
            }
        }

        return depths;
    }

    /// <summary>
    /// Builds a map from each package key to the dependency keys of its direct children as recorded in the lock file.
    /// </summary>
    private static Dictionary<string, string[]> BuildDependencyKeys(LockFile lockFile)
    {
        Dictionary<string, string[]> result = new(StringComparer.OrdinalIgnoreCase);
        LockFileTarget? target = lockFile.Targets.FirstOrDefault();
        if (target is null)
        {
            return result;
        }

        foreach (LockFileTargetLibrary library in target.Libraries)
        {
            string[] dependencyKeys = library.Dependencies
                .Select(dependency => target.Libraries.FirstOrDefault(l =>
                    string.Equals(l.Name, dependency.Id, StringComparison.OrdinalIgnoreCase)))
                .Where(dependencyLibrary => dependencyLibrary is not null)
                .Select(dependencyLibrary => PackageInfo.CreatePackageKey(dependencyLibrary!.Name!, dependencyLibrary.Version!.ToNormalizedString()))
                .ToArray();

            if (library.Version is not null && !string.IsNullOrWhiteSpace(library.Name))
            {
                result[PackageInfo.CreatePackageKey(library.Name!, library.Version.ToNormalizedString())] = dependencyKeys;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the set of package keys for packages that have at least one direct dependency on a pre-1.0 (major == 0) package.
    /// </summary>
    private static HashSet<string> FindPackagesDependingOnPreOneZeroPackages(LockFile lockFile)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        LockFileTarget? target = lockFile.Targets.FirstOrDefault();
        if (target is null)
        {
            return result;
        }

        foreach (LockFileTargetLibrary library in target.Libraries)
        {
            bool hasPreOneZeroDependency = library.Dependencies
                .Select(dependency => target.Libraries.FirstOrDefault(l =>
                    string.Equals(l.Name, dependency.Id, StringComparison.OrdinalIgnoreCase)))
                .Any(dependencyLibrary => dependencyLibrary?.Version is not null && dependencyLibrary.Version.Major == 0);

            if (hasPreOneZeroDependency && library.Version is not null && !string.IsNullOrWhiteSpace(library.Name))
            {
                result.Add(PackageInfo.CreatePackageKey(library.Name!, library.Version.ToNormalizedString()));
            }
        }

        return result;
    }

}
