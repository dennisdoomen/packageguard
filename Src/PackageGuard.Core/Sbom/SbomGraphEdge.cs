namespace PackageGuard.Core.Sbom;

/// <summary>
/// A directed edge in the dependency graph, from a package to one of its direct dependencies.
/// Only present for ecosystems with an accurate parent-child graph (see <see cref="SbomModel.EcosystemGraphIsAccurate"/>).
/// </summary>
/// <param name="FromKey">The dependency key of the depending package.</param>
/// <param name="ToKey">The dependency key of the depended-upon package.</param>
internal sealed record SbomGraphEdge(string FromKey, string ToKey);
