namespace PackageGuard.Core.Sbom;

/// <summary>
/// A single component (package) to be rendered into an SBOM, derived from a <see cref="PackageInfo"/>.
/// Both the CycloneDX and SPDX writers consume this instead of <see cref="PackageInfo"/> directly, so
/// purl construction, license-evidence classification, and vulnerability shaping happen exactly once.
/// </summary>
internal sealed class SbomComponent
{
    /// <summary>
    /// Gets the dependency-graph key (<c>ecosystem|name|version</c>) that identifies this component.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the Package URL (purl) that identifies this component.
    /// </summary>
    public required string Purl { get; init; }

    /// <summary>
    /// Gets the package identifier as exposed by the package ecosystem.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the resolved package version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the resolved license identifier or friendly license name, or <see langword="null"/> when unknown.
    /// </summary>
    public string? License { get; init; }

    /// <summary>
    /// Gets the provenance of <see cref="License"/>: whether it was declared by the package's own metadata
    /// or concluded from external evidence such as a GitHub repository scan.
    /// </summary>
    public LicenseEvidence LicenseEvidence { get; init; }

    /// <summary>
    /// Gets the source URL where the license text or license metadata can be retrieved.
    /// </summary>
    public string? LicenseUrl { get; init; }

    /// <summary>
    /// Gets the repository URL associated with the package, when known.
    /// </summary>
    public string? RepositoryUrl { get; init; }

    /// <summary>
    /// Gets whether this component is a direct dependency of one of the analyzed projects, as opposed to
    /// a transitive dependency.
    /// </summary>
    public bool IsDirect { get; init; }

    /// <summary>
    /// Gets the project files that reference this component.
    /// </summary>
    public IReadOnlyList<string> Projects { get; init; } = [];

    /// <summary>
    /// Gets the OSV vulnerability records known for this component. Only populated when <c>--report-risk</c>
    /// was passed in the same run; otherwise empty.
    /// </summary>
    public IReadOnlyList<OsvVulnerabilityRecord> Vulnerabilities { get; init; } = [];
}
