namespace PackageGuard.Core;

/// <summary>
/// Describes the provenance of a package's resolved <see cref="PackageInfo.License"/>.
/// </summary>
public enum LicenseEvidence
{
    /// <summary>
    /// No license evidence has been recorded, or the license itself is unknown.
    /// </summary>
    Unknown,

    /// <summary>
    /// The license was declared by the package's own metadata (NuGet API, .nuspec, npm registry/lock file),
    /// or corrected to the actual publisher-declared license by a known-good override.
    /// </summary>
    Declared,

    /// <summary>
    /// The license was concluded from external evidence rather than the package's own metadata, such as
    /// a GitHub repository license scan or a heuristic match against downloaded license text.
    /// </summary>
    Concluded
}
