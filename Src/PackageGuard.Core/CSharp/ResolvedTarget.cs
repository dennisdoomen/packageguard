using NuGet.LibraryModel;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace PackageGuard.Core.CSharp;

/// <summary>
/// One project resolved for one target framework: the libraries NuGet picked, plus the direct dependencies
/// the project itself declared.
/// </summary>
/// <remarks>
/// Within a single <see cref="LockFileTarget" /> a package appears exactly once, so libraries are keyed by
/// name alone and no version needs to be part of the key.
/// </remarks>
internal sealed class ResolvedTarget
{
    private readonly LockFileTarget target;
    private readonly Dictionary<string, HashSet<string>> reachableByRoot = new(StringComparer.OrdinalIgnoreCase);

    public ResolvedTarget(LockFile lockFile, LockFileTarget target)
    {
        this.target = target;

        ProjectName = lockFile.PackageSpec.Name ?? "";
        TargetFramework = target.TargetFramework.GetShortFolderName();

        Libraries = target.Libraries
            .Where(library => library.Name is not null)
            .ToDictionary(library => library.Name!, StringComparer.OrdinalIgnoreCase);

        DirectPackages = GetDirectPackages(lockFile, target);
        DirectProjects = GetDirectProjects(lockFile, target);
    }

    /// <summary>
    /// Gets the name of the project this target belongs to.
    /// </summary>
    public string ProjectName { get; }

    /// <summary>
    /// Gets the short target framework, such as <c>net9.0</c>.
    /// </summary>
    public string TargetFramework { get; }

    /// <summary>
    /// Gets every library NuGet resolved for this target, keyed by package name.
    /// </summary>
    public IReadOnlyDictionary<string, LockFileTargetLibrary> Libraries { get; }

    private IReadOnlyCollection<LibraryDependency> DirectPackages { get; }

    private IReadOnlyCollection<string> DirectProjects { get; }

    /// <summary>
    /// Gets the resolved libraries that are NuGet packages rather than project references.
    /// </summary>
    public IEnumerable<LockFileTargetLibrary> PackageLibraries =>
        target.Libraries.Where(library => library.Name is not null && library.Version is not null && IsPackage(library));

    /// <summary>
    /// Gets the direct package references that are a candidate for removal.
    /// </summary>
    /// <remarks>
    /// An implicit reference, or one marked <c>PrivateAssets="all"</c>, is deliberately left out. It is not a
    /// dependency of what this project ships, so it must never be reported or treated as a provider.
    /// </remarks>
    public IEnumerable<LibraryDependency> RemovableDirectPackages => DirectPackages.Where(IsRemovable);

    /// <summary>
    /// Gets the names of the direct dependencies that could already provide another package.
    /// </summary>
    public IEnumerable<string> ProviderRoots =>
        RemovableDirectPackages.Select(dependency => dependency.Name).Concat(DirectProjects);

    /// <summary>
    /// Determines whether the named library is a referenced project rather than a NuGet package.
    /// </summary>
    public bool IsProject(string name) =>
        Libraries.TryGetValue(name, out LockFileTargetLibrary? library) && !IsPackage(library);

    /// <summary>
    /// Finds the direct dependencies of this project that already provide the named package.
    /// </summary>
    /// <returns>
    /// The providing projects first, then the providing packages, or an empty collection when the
    /// reference is the only thing bringing the package in.
    /// </returns>
    public IReadOnlyCollection<string> FindProvidersOf(string packageName)
    {
        return
        [
            .. ProviderRoots
                .Where(root => !string.Equals(root, packageName, StringComparison.OrdinalIgnoreCase))
                .Where(root => GetReachablePackages(root).Contains(packageName))
                // Projects first: removing a reference a sibling project already provides is the clearer case.
                .OrderByDescending(IsProject)
                .ThenBy(root => root, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// Walks the resolved graph from the named dependency and returns everything reachable from it.
    /// </summary>
    /// <remarks>
    /// Each root is walked at most once. The reachable set of a provider never depends on which reference
    /// is being checked against it, so the result is cached for the lifetime of this target.
    /// </remarks>
    private HashSet<string> GetReachablePackages(string root)
    {
        if (reachableByRoot.TryGetValue(root, out HashSet<string>? cached))
        {
            return cached;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            if (!Libraries.TryGetValue(pending.Dequeue(), out LockFileTargetLibrary? library))
            {
                continue;
            }

            foreach (PackageDependency dependency in library.Dependencies.Where(x => visited.Add(x.Id)))
            {
                pending.Enqueue(dependency.Id);
            }
        }

        reachableByRoot[root] = visited;

        return visited;
    }

    /// <summary>
    /// Determines the version the package would resolve to if the direct reference were removed.
    /// </summary>
    /// <returns>
    /// The lower version that would be resolved, or <c>null</c> when removing the reference changes nothing.
    /// </returns>
    public NuGetVersion? GetVersionWithoutDirectReference(LibraryDependency dependency)
    {
        NuGetVersion? declared = dependency.LibraryRange.VersionRange?.MinVersion;
        if (declared is null)
        {
            return null;
        }

        NuGetVersion? highestTransitive = target.Libraries
            .SelectMany(library => library.Dependencies)
            .Where(edge => string.Equals(edge.Id, dependency.Name, StringComparison.OrdinalIgnoreCase))
            .Select(edge => edge.VersionRange?.MinVersion)
            .Where(version => version is not null)
            .DefaultIfEmpty(null)
            .Max();

        return highestTransitive is not null && declared > highestTransitive ? highestTransitive : null;
    }

    private static bool IsPackage(LockFileTargetLibrary library) =>
        !string.Equals(library.Type, "project", StringComparison.OrdinalIgnoreCase);

    private static bool IsRemovable(LibraryDependency dependency) =>
        !dependency.AutoReferenced && dependency.SuppressParent != LibraryIncludeFlags.All;

    private static IReadOnlyCollection<LibraryDependency> GetDirectPackages(LockFile lockFile, LockFileTarget target)
    {
        TargetFrameworkInformation? framework = lockFile.PackageSpec.TargetFrameworks
            .FirstOrDefault(x => x.FrameworkName.Equals(target.TargetFramework));

        return framework?.Dependencies ?? [];
    }

    private static IReadOnlyCollection<string> GetDirectProjects(LockFile lockFile, LockFileTarget target)
    {
        ProjectRestoreMetadataFrameworkInfo? framework = lockFile.PackageSpec.RestoreMetadata?.TargetFrameworks
            .FirstOrDefault(x => x.FrameworkName.Equals(target.TargetFramework));

        if (framework is null)
        {
            return [];
        }

        return
        [
            .. framework.ProjectReferences
                .Select(reference => Path.GetFileNameWithoutExtension(reference.ProjectPath))
                .Where(name => !string.IsNullOrEmpty(name))
        ];
    }
}
