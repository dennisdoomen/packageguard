using NuGet.ProjectModel;
using NuGet.Versioning;

namespace PackageGuard.Core.CSharp;

/// <summary>
/// A single hop in a dependency chain: the package at this hop, and the version range its predecessor
/// (the previous hop, or the project itself for the first hop) requested for it.
/// </summary>
internal sealed record DependencyHop(string Name, string Version, string? RequestedRange);

/// <summary>
/// A chain of packages from a project's direct dependency down to a target package. <see cref="Hops"/>[0]
/// is always a direct dependency of the project.
/// </summary>
internal sealed record DependencyChain(IReadOnlyList<DependencyHop> Hops);

/// <summary>
/// Finds the dependency chains that pull a specific package version into a project, using the parent-child
/// graph already captured in the project's restored lock file. Only meaningful for NuGet, whose lock file
/// records an accurate graph; npm/yarn/pnpm lock-file parsers do not (see <c>SbomModelBuilder</c>).
/// </summary>
internal static class NuGetDependencyPathFinder
{
    /// <summary>
    /// Returns one chain per direct dependency of the project that transitively (or directly) pulls in the
    /// package identified by <paramref name="targetName"/> and <paramref name="targetVersion"/>. Each chain
    /// is the shortest path found from that direct dependency to the target.
    /// </summary>
    public static IReadOnlyList<DependencyChain> FindChains(LockFile lockFile, string targetName, string targetVersion)
    {
        LockFileTarget? target = lockFile.Targets.FirstOrDefault();
        if (target is null)
        {
            return [];
        }

        Dictionary<string, LockFileTargetLibrary> librariesByName = BuildLibraryIndex(target);

        List<DependencyChain> chains = new();

        // A multi-targeted project (e.g. net9.0;net10.0) repeats the same direct dependency once per target
        // framework; only the first restored framework's libraries are indexed above, so only its dependency
        // declaration is relevant here.
        var directDependencies = lockFile.PackageSpec.TargetFrameworks
            .SelectMany(tf => tf.Dependencies)
            .GroupBy(dependency => dependency.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

        foreach (var directDependency in directDependencies)
        {
            if (string.IsNullOrWhiteSpace(directDependency.Name) ||
                !librariesByName.TryGetValue(directDependency.Name, out LockFileTargetLibrary? rootLibrary) ||
                rootLibrary.Version is null)
            {
                continue;
            }

            var rootHop = new DependencyHop(rootLibrary.Name!, rootLibrary.Version.ToNormalizedString(),
                FormatRange(directDependency.LibraryRange.VersionRange));

            if (IsMatch(rootLibrary, targetName, targetVersion))
            {
                chains.Add(new DependencyChain([rootHop]));
                continue;
            }

            List<DependencyHop>? remainingHops = FindShortestPathToTarget(rootLibrary, librariesByName, targetName, targetVersion);
            if (remainingHops is not null)
            {
                chains.Add(new DependencyChain(remainingHops.Prepend(rootHop).ToArray()));
            }
        }

        return chains;
    }

    /// <summary>
    /// Builds a lookup from package name to its resolved library entry for the first (and typically only)
    /// restored target framework.
    /// </summary>
    private static Dictionary<string, LockFileTargetLibrary> BuildLibraryIndex(LockFileTarget target)
    {
        return target.Libraries
            .Where(library => !string.IsNullOrWhiteSpace(library.Name))
            .GroupBy(library => library.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs a breadth-first search over <paramref name="root"/>'s dependencies for the shortest path
    /// that reaches the target package, returning the hops after (and excluding) <paramref name="root"/>.
    /// </summary>
    private static List<DependencyHop>? FindShortestPathToTarget(LockFileTargetLibrary root,
        IReadOnlyDictionary<string, LockFileTargetLibrary> librariesByName, string targetName, string targetVersion)
    {
        Queue<(LockFileTargetLibrary Library, List<DependencyHop> PathSoFar)> queue = new();
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { root.Name! };

        EnqueueChildren(root, librariesByName, [], queue, visited);

        while (queue.Count > 0)
        {
            (LockFileTargetLibrary library, List<DependencyHop> path) = queue.Dequeue();

            if (IsMatch(library, targetName, targetVersion))
            {
                return path;
            }

            EnqueueChildren(library, librariesByName, path, queue, visited);
        }

        return null;
    }

    /// <summary>
    /// Enqueues every not-yet-visited dependency of <paramref name="library"/>, each carrying the path
    /// travelled so far plus the new hop for that dependency.
    /// </summary>
    private static void EnqueueChildren(LockFileTargetLibrary library, IReadOnlyDictionary<string, LockFileTargetLibrary> librariesByName,
        List<DependencyHop> pathSoFar, Queue<(LockFileTargetLibrary, List<DependencyHop>)> queue, HashSet<string> visited)
    {
        foreach (var dependency in library.Dependencies)
        {
            if (!librariesByName.TryGetValue(dependency.Id, out LockFileTargetLibrary? child) ||
                child.Version is null ||
                !visited.Add(child.Name!))
            {
                continue;
            }

            var hop = new DependencyHop(child.Name!, child.Version.ToNormalizedString(), FormatRange(dependency.VersionRange));
            queue.Enqueue((child, [..pathSoFar, hop]));
        }
    }

    private static bool IsMatch(LockFileTargetLibrary library, string targetName, string targetVersion)
    {
        return !string.IsNullOrWhiteSpace(library.Name) &&
               library.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase) &&
               library.Version is not null &&
               library.Version.ToNormalizedString().Equals(targetVersion, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Formats a requested version range for display, preferring the author-written form over the
    /// normalized one.
    /// </summary>
    private static string? FormatRange(VersionRange? range)
    {
        if (range is null)
        {
            return null;
        }

        return range.OriginalString ?? range.ToNormalizedString();
    }
}
