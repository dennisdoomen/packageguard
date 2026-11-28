using PackageGuard.Core.Package;

namespace PackageGuard.Core.Policy;

/// <summary>
/// Allows an exact npm package artifact to run install scripts.
/// </summary>
internal sealed record InstallScriptException(
    string Package,
    string Version,
    string? Integrity,
    string? Resolved)
{
    /// <summary>
    /// Determines whether the exception identifies the exact package version and artifact.
    /// </summary>
    internal bool Matches(PackageInfo package) =>
        package.Name.Equals(Package, StringComparison.OrdinalIgnoreCase) &&
        package.Version.Equals(Version, StringComparison.OrdinalIgnoreCase) &&
        !package.HasConflictingArtifacts &&
        (Integrity is not null && string.Equals(package.PackageIntegrity, Integrity, StringComparison.Ordinal) ||
         Resolved is not null && string.Equals(package.ArtifactUrl, Resolved, StringComparison.Ordinal));
}
