using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace PackageGuard.Core.CSharp;

/// <summary>
/// Finds redundant package references and version conflicts in the dependency graph that NuGet resolved
/// during restore.
/// </summary>
/// <remarks>
/// A direct package reference is redundant when the same package is already reachable through another
/// direct dependency of that project. That dependency is either a referenced project
/// (<see cref="DependencyFindingKind.RedundantViaProject" />) or another package
/// (<see cref="DependencyFindingKind.RedundantViaPackage" />).
/// <para>
/// This works off the restore output only, so it never reaches the network and never evaluates MSBuild.
/// It also means it sees the graph, not your source code: a package can be redundant in the graph and
/// still be used directly in code, so findings are reported and never applied automatically.
/// </para>
/// </remarks>
public sealed class DependencyHygieneAnalyzer
{
    /// <summary>
    /// Gets or sets the package ids to leave out of the results.
    /// </summary>
    public IReadOnlyCollection<string> ExcludedPackageIds { get; set; } = [];

    /// <summary>
    /// Finds redundant package references and version conflicts across the supplied lock files.
    /// </summary>
    /// <param name="lockFiles">The restore output of the projects to analyze.</param>
    /// <returns>The findings, or an empty collection when the graph is clean.</returns>
    public IReadOnlyCollection<DependencyFinding> Analyze(IReadOnlyCollection<LockFile> lockFiles)
    {
        ArgumentNullException.ThrowIfNull(lockFiles);

        ResolvedTarget[] targets = lockFiles.SelectMany(GetResolvedTargets).ToArray();

        var findings = new List<DependencyFinding>();

        foreach (ResolvedTarget target in targets)
        {
            findings.AddRange(FindRedundantReferences(target));
        }

        findings.AddRange(FindVersionConflicts(targets));

        return findings;
    }

    private static IEnumerable<ResolvedTarget> GetResolvedTargets(LockFile lockFile)
    {
        // Runtime-specific targets repeat the framework graph per RID, which would report every finding twice.
        foreach (LockFileTarget target in lockFile.Targets.Where(x => string.IsNullOrEmpty(x.RuntimeIdentifier)))
        {
            yield return new ResolvedTarget(lockFile, target);
        }
    }

    private IEnumerable<DependencyFinding> FindRedundantReferences(ResolvedTarget target)
    {
        // Every provider's reachable set is independent of the reference being checked, so compute it once.
        var reachableByRoot = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (LibraryDependency candidate in target.RemovableDirectPackages)
        {
            if (IsExcluded(candidate.Name) || !target.Libraries.ContainsKey(candidate.Name))
            {
                continue;
            }

            string[] providers = FindProviders(target, candidate.Name, reachableByRoot);
            if (providers.Length == 0)
            {
                continue;
            }

            yield return CreateRedundancyFinding(target, candidate, providers);
        }
    }

    private static string[] FindProviders(ResolvedTarget target, string packageName,
        Dictionary<string, HashSet<string>> reachableByRoot)
    {
        var providers = new List<string>();

        foreach (string root in target.ProviderRoots.Where(x => !NameEquals(x, packageName)))
        {
            if (!reachableByRoot.TryGetValue(root, out HashSet<string>? reachable))
            {
                reachableByRoot[root] = reachable = target.GetReachablePackages(root);
            }

            if (reachable.Contains(packageName))
            {
                providers.Add(root);
            }
        }

        // Report the projects first, because removing a reference a sibling project provides is the clearer case.
        return [.. providers.OrderByDescending(target.IsProject).ThenBy(x => x, StringComparer.OrdinalIgnoreCase)];
    }

    private static DependencyFinding CreateRedundancyFinding(ResolvedTarget target, LibraryDependency candidate,
        string[] providers)
    {
        bool viaProject = providers.Any(target.IsProject);
        string resolvedVersion = target.Libraries[candidate.Name].Version?.ToNormalizedString() ?? "";
        NuGetVersion? wouldResolveTo = target.GetVersionWithoutDirectReference(candidate);

        string description =
            $"{candidate.Name} is already provided by {(viaProject ? "project" : "package")} " +
            $"{string.Join(", ", providers)}.";

        if (wouldResolveTo is not null)
        {
            description += $" Removing it lowers {candidate.Name} from {resolvedVersion} to " +
                $"{wouldResolveTo.ToNormalizedString()}.";
        }

        return new DependencyFinding
        {
            Kind = viaProject ? DependencyFindingKind.RedundantViaProject : DependencyFindingKind.RedundantViaPackage,
            Project = target.ProjectName,
            TargetFramework = target.TargetFramework,
            PackageId = candidate.Name,
            ResolvedVersion = resolvedVersion,
            DeclaredVersion = Normalize(candidate.LibraryRange.VersionRange),
            SafeToRemove = wouldResolveTo is null,
            WouldResolveTo = wouldResolveTo?.ToNormalizedString() ?? "",
            Providers = providers,
            Description = description
        };
    }

    private IEnumerable<DependencyFinding> FindVersionConflicts(IReadOnlyCollection<ResolvedTarget> targets)
    {
        // Grouped per target framework, so a solution that legitimately resolves differently per framework
        // is not reported as a conflict.
        var versionsPerPackage = new Dictionary<string, SortedDictionary<string, SortedSet<string>>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, ResolvedTarget> group in targets.GroupBy(x => x.TargetFramework,
            StringComparer.OrdinalIgnoreCase))
        {
            MergeConflictsWithin(group, versionsPerPackage);
        }

        return versionsPerPackage.Select(CreateConflictFinding).ToArray();
    }

    private void MergeConflictsWithin(IEnumerable<ResolvedTarget> targetsOfOneFramework,
        Dictionary<string, SortedDictionary<string, SortedSet<string>>> accumulator)
    {
        var withinFramework = new Dictionary<string, SortedDictionary<string, SortedSet<string>>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (ResolvedTarget target in targetsOfOneFramework)
        {
            foreach (LockFileTargetLibrary library in target.PackageLibraries.Where(x => !IsExcluded(x.Name!)))
            {
                SortedDictionary<string, SortedSet<string>> versions = withinFramework.GetOrAdd(library.Name!);
                versions.GetOrAdd(library.Version!.ToNormalizedString()).Add(target.ProjectName);
            }
        }

        foreach (KeyValuePair<string, SortedDictionary<string, SortedSet<string>>> pair in withinFramework
            .Where(x => x.Value.Count > 1))
        {
            SortedDictionary<string, SortedSet<string>> merged = accumulator.GetOrAdd(pair.Key);
            foreach (KeyValuePair<string, SortedSet<string>> version in pair.Value)
            {
                merged.GetOrAdd(version.Key).UnionWith(version.Value);
            }
        }
    }

    private static DependencyFinding CreateConflictFinding(
        KeyValuePair<string, SortedDictionary<string, SortedSet<string>>> conflict)
    {
        string[] versions = [.. conflict.Value.Keys.OrderByDescending(ParseOrZero)];

        string detail = string.Join("; ",
            versions.Select(version => $"{version} ({string.Join(", ", conflict.Value[version])})"));

        return new DependencyFinding
        {
            Kind = DependencyFindingKind.VersionConflict,
            PackageId = conflict.Key,
            ResolvedVersion = versions[0],
            Providers = versions,
            Description = $"{conflict.Key} resolves to multiple versions: {detail}."
        };
    }

    private bool IsExcluded(string packageName) =>
        ExcludedPackageIds.Contains(packageName, StringComparer.OrdinalIgnoreCase);

    private static bool NameEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static NuGetVersion ParseOrZero(string version) =>
        NuGetVersion.TryParse(version, out NuGetVersion? parsed) ? parsed : new NuGetVersion(0, 0, 0);

    private static string Normalize(VersionRange? range) =>
        range?.MinVersion?.ToNormalizedString() ?? range?.OriginalString ?? "";
}
