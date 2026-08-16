namespace PackageGuard.Core.Sbom;

/// <summary>
/// Describes the synthetic root component that represents the analyzed solution or project in an SBOM,
/// since PackageGuard aggregates across every project into a single document.
/// </summary>
/// <param name="BomRef">The stable identifier used to reference this root from dependency/relationship entries.</param>
/// <param name="Name">The display name for the root component, derived from the resolved solution/project file.</param>
internal sealed record SbomRootComponent(string BomRef, string Name);
