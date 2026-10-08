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
        // A lock file without a project section declares no direct dependencies, so there is nothing to check.
        if (lockFile.PackageSpec is null)
        {
            yield break;
        }

        // Runtime-specific targets repeat the framework graph per RID, which would report every finding twice.
        foreach (LockFileTarget target in lockFile.Targets.Where(x => string.IsNullOrEmpty(x.RuntimeIdentifier)))
        {
            yield return new ResolvedTarget(lockFile, target);
        }
    }

    private IEnumerable<DependencyFinding> FindRedundantReferences(ResolvedTarget target)
    {
        foreach (LibraryDependency candidate in target.RemovableDirectPackages)
        {
            if (IsExcluded(candidate.Name) || !target.Libraries.ContainsKey(candidate.Name))
            {
                continue;
            }

            IReadOnlyCollection<string> providers = target.FindProvidersOf(candidate.Name);
            if (providers.Count > 0)
            {
                yield return CreateRedundancyFinding(target, candidate, providers);
            }
        }
    }

    private static DependencyFinding CreateRedundancyFinding(ResolvedTarget target, LibraryDependency candidate,
        IReadOnlyCollection<string> providers)
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
        // Grouped per target framework first, so a solution that legitimately resolves differently per
        // framework is not reported as a conflict.
        return targets
            .GroupBy(target => target.TargetFramework, StringComparer.OrdinalIgnoreCase)
            .SelectMany(FindConflictsWithin)
            .GroupBy(resolved => resolved.Package, StringComparer.OrdinalIgnoreCase)
            .Select(CreateConflictFinding);
    }

    private IEnumerable<ResolvedPackage> FindConflictsWithin(IEnumerable<ResolvedTarget> targetsOfOneFramework)
    {
        return targetsOfOneFramework
            .SelectMany(target => target.PackageLibraries
                .Where(library => !IsExcluded(library.Name!))
                .Select(library => new ResolvedPackage(library.Name!, library.Version!.ToNormalizedString(),
                    target.ProjectName)))
            .GroupBy(resolved => resolved.Package, StringComparer.OrdinalIgnoreCase)
            .Where(HasMoreThanOneVersion)
            .SelectMany(group => group);
    }

    private static bool HasMoreThanOneVersion(IEnumerable<ResolvedPackage> resolved) =>
        resolved.Select(x => x.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

    private static DependencyFinding CreateConflictFinding(IGrouping<string, ResolvedPackage> conflict)
    {
        IGrouping<string, ResolvedPackage>[] byVersion =
        [
            .. conflict
                .GroupBy(resolved => resolved.Version, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => ParseOrZero(group.Key))
        ];

        string detail = string.Join("; ", byVersion.Select(DescribeVersion));

        return new DependencyFinding
        {
            Kind = DependencyFindingKind.VersionConflict,
            PackageId = conflict.Key,
            ResolvedVersion = byVersion[0].Key,
            ConflictingVersions = [.. byVersion.Select(group => group.Key)],
            Description = $"{conflict.Key} resolves to multiple versions: {detail}."
        };
    }

    private static string DescribeVersion(IGrouping<string, ResolvedPackage> version)
    {
        IOrderedEnumerable<string> projects = version
            .Select(resolved => resolved.Project)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(project => project, StringComparer.OrdinalIgnoreCase);

        return $"{version.Key} ({string.Join(", ", projects)})";
    }

    private bool IsExcluded(string packageName) =>
        ExcludedPackageIds.Contains(packageName, StringComparer.OrdinalIgnoreCase);

    private static NuGetVersion ParseOrZero(string version) =>
        NuGetVersion.TryParse(version, out NuGetVersion? parsed) ? parsed : new NuGetVersion(0, 0, 0);

    private static string Normalize(VersionRange? range) =>
        range?.MinVersion?.ToNormalizedString() ?? range?.OriginalString ?? "";

    /// <summary>
    /// One package, at the version a single project resolved it to.
    /// </summary>
    private sealed record ResolvedPackage(string Package, string Version, string Project);
}
