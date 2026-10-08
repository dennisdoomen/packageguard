namespace PackageGuard.Core.CSharp;

/// <summary>
/// The category of a <see cref="DependencyFinding" />.
/// </summary>
public enum DependencyFindingKind
{
    /// <summary>
    /// A direct package reference that a referenced project already provides.
    /// </summary>
    RedundantViaProject,

    /// <summary>
    /// A direct package reference that another direct package reference already provides.
    /// </summary>
    RedundantViaPackage,

    /// <summary>
    /// The same package resolves to different versions across the analyzed projects.
    /// </summary>
    VersionConflict
}

/// <summary>
/// A single dependency hygiene problem found in the resolved dependency graph of a restored project.
/// </summary>
public sealed class DependencyFinding
{
    /// <summary>
    /// Gets the category of the problem.
    /// </summary>
    public DependencyFindingKind Kind { get; init; }

    /// <summary>
    /// Gets the name of the project this finding applies to, or an empty string for a version conflict,
    /// which spans projects.
    /// </summary>
    public string Project { get; init; } = "";

    /// <summary>
    /// Gets the short target framework the finding was detected for, such as <c>net9.0</c>.
    /// </summary>
    public string TargetFramework { get; init; } = "";

    /// <summary>
    /// Gets the id of the package this finding applies to.
    /// </summary>
    public string PackageId { get; init; } = "";

    /// <summary>
    /// Gets the version NuGet resolved for the package. For a version conflict this is the highest
    /// of <see cref="ConflictingVersions" />.
    /// </summary>
    public string ResolvedVersion { get; init; } = "";

    /// <summary>
    /// Gets the lowest version the project asked for, normalized from its version range.
    /// </summary>
    public string DeclaredVersion { get; init; } = "";

    /// <summary>
    /// Gets a value indicating whether removing the reference keeps the same resolved version.
    /// When <c>false</c>, removing it would lower the version and the reference needs a human decision.
    /// </summary>
    public bool SafeToRemove { get; init; }

    /// <summary>
    /// Gets the version the package would resolve to if the reference were removed, or an empty string
    /// when removing it changes nothing.
    /// </summary>
    public string WouldResolveTo { get; init; } = "";

    /// <summary>
    /// Gets the projects or packages that already provide this package. Only set on a redundancy finding.
    /// </summary>
    public IReadOnlyCollection<string> Providers { get; init; } = [];

    /// <summary>
    /// Gets the versions the package resolves to, highest first. Only set on a
    /// <see cref="DependencyFindingKind.VersionConflict" />.
    /// </summary>
    public IReadOnlyCollection<string> ConflictingVersions { get; init; } = [];

    /// <summary>
    /// Gets a sentence describing the finding, suitable for showing to a user.
    /// </summary>
    public string Description { get; init; } = "";
}
