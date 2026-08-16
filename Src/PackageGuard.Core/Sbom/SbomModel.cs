namespace PackageGuard.Core.Sbom;

/// <summary>
/// The shared, format-agnostic SBOM data model computed once from resolved package metadata and rendered
/// by each format-specific writer (CycloneDX, SPDX).
/// </summary>
internal sealed class SbomModel
{
    /// <summary>
    /// Gets the synthetic root component representing the analyzed solution.
    /// </summary>
    public required SbomRootComponent Root { get; init; }

    /// <summary>
    /// Gets every component (package) discovered across all analyzed projects.
    /// </summary>
    public required IReadOnlyList<SbomComponent> Components { get; init; }

    /// <summary>
    /// Gets the known parent-to-child dependency edges. Only populated for ecosystems whose
    /// <see cref="EcosystemGraphIsAccurate"/> entry is <see langword="true"/>.
    /// </summary>
    public required IReadOnlyList<SbomGraphEdge> Edges { get; init; }

    /// <summary>
    /// Gets, per ecosystem, whether a real parent-child dependency graph is available. Currently only
    /// NuGet builds a real graph; npm/yarn/pnpm packages are recorded as direct/flat pending real
    /// dependency-graph parsing for those ecosystems.
    /// </summary>
    public required IReadOnlyDictionary<string, bool> EcosystemGraphIsAccurate { get; init; }

    /// <summary>
    /// Gets the timestamp at which this model was generated.
    /// </summary>
    public required DateTimeOffset GeneratedAt { get; init; }
}
