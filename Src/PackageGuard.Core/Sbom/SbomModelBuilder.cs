namespace PackageGuard.Core.Sbom;

/// <summary>
/// Builds a shared <see cref="SbomModel"/> from resolved package metadata, computing purls, the
/// dependency graph, and license/vulnerability shaping once so that every SBOM format writer renders
/// exactly the same data.
/// </summary>
internal static class SbomModelBuilder
{
    /// <summary>
    /// Ecosystems for which a real parent-to-child dependency graph is currently available. Only NuGet
    /// builds real dependency edges today; npm/yarn/pnpm lock-file parsers do not.
    /// </summary>
    private static readonly HashSet<string> EcosystemsWithAccurateGraph = new(StringComparer.OrdinalIgnoreCase) { "nuget" };

    /// <summary>
    /// Builds the shared SBOM model from every package used across the analyzed solution.
    /// </summary>
    public static SbomModel Build(IReadOnlyCollection<PackageInfo> packages, string projectPath)
    {
        IReadOnlyDictionary<string, PackageInfo> packagesByKey = packages
            .GroupBy(package => package.CreatePackageKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return new SbomModel
        {
            Root = new SbomRootComponent("solution-root", ResolveRootName(projectPath)),
            Components = packages.Select(BuildComponent).ToArray(),
            Edges = BuildEdges(packages, packagesByKey),
            EcosystemGraphIsAccurate = ComputeGraphAccuracy(packages),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Projects a single <see cref="PackageInfo"/> into its <see cref="SbomComponent"/> representation.
    /// </summary>
    private static SbomComponent BuildComponent(PackageInfo package)
    {
        string ecosystem = package.GetPackageEcosystem();

        return new SbomComponent
        {
            Key = package.CreatePackageKey(),
            Purl = PackageUrlBuilder.Build(ecosystem, package.Name, package.Version),
            Name = package.Name,
            Version = package.Version,
            License = package.License,
            LicenseEvidence = package.LicenseEvidence,
            LicenseUrl = package.LicenseUrl,
            RepositoryUrl = package.RepositoryUrl,
            IsDirect = package.DependencyDepth <= 1,
            Projects = package.Projects,
            Vulnerabilities = package.Vulnerabilities
        };
    }

    /// <summary>
    /// Resolves each package's <see cref="PackageInfo.DependencyKeys"/> into graph edges, but only for
    /// ecosystems with an accurate dependency graph (see <see cref="EcosystemsWithAccurateGraph"/>), so
    /// the model never fabricates edges that don't reflect reality.
    /// </summary>
    private static IReadOnlyList<SbomGraphEdge> BuildEdges(IReadOnlyCollection<PackageInfo> packages,
        IReadOnlyDictionary<string, PackageInfo> packagesByKey)
    {
        List<SbomGraphEdge> edges = [];

        foreach (PackageInfo package in packages)
        {
            if (!EcosystemsWithAccurateGraph.Contains(package.GetPackageEcosystem()))
            {
                continue;
            }

            string fromKey = package.CreatePackageKey();
            foreach (string childKey in package.DependencyKeys.Where(packagesByKey.ContainsKey))
            {
                edges.Add(new SbomGraphEdge(fromKey, childKey));
            }
        }

        return edges;
    }

    /// <summary>
    /// Computes, per ecosystem present in <paramref name="packages"/>, whether a real dependency graph
    /// is available.
    /// </summary>
    private static IReadOnlyDictionary<string, bool> ComputeGraphAccuracy(IReadOnlyCollection<PackageInfo> packages)
    {
        return packages
            .Select(package => package.GetPackageEcosystem())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(ecosystem => ecosystem, ecosystem => EcosystemsWithAccurateGraph.Contains(ecosystem),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the display name for the synthetic root component from the best-matching solution,
    /// project, or package manifest file found at <paramref name="projectPath"/>.
    /// </summary>
    private static string ResolveRootName(string projectPath)
    {
        if (File.Exists(projectPath))
        {
            return Path.GetFileNameWithoutExtension(projectPath);
        }

        if (!Directory.Exists(projectPath))
        {
            return Path.GetFileNameWithoutExtension(projectPath);
        }

        string? candidate = Directory.EnumerateFiles(projectPath, "*.sln").FirstOrDefault()
            ?? Directory.EnumerateFiles(projectPath, "*.slnx").FirstOrDefault()
            ?? Directory.EnumerateFiles(projectPath, "*.csproj").FirstOrDefault()
            ?? Directory.EnumerateFiles(projectPath, "package.json").FirstOrDefault();

        return candidate is not null
            ? Path.GetFileNameWithoutExtension(candidate)
            : new DirectoryInfo(projectPath).Name;
    }
}
