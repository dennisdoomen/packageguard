namespace PackageGuard.Core.Policy;

public class PackageSelector(string id)
{
    public PackageSelector(string id, string versionRange) : this(id)
    {
        VersionRange = versionRange.Length > 0 ? versionRange : null;
    }

    /// <summary>
    /// The ID of the package, e.g. Newtonsoft.Json
    /// </summary>
    public string Id { get; set; } = id;

    /// <summary>
    /// Defines a version or version range using NuGet versioning format, e.g.
    /// </summary>
    public string? VersionRange { get; set; }

    /// <summary>
    /// Gets or sets the path of the configuration file this rule was loaded from, when known.
    /// </summary>
    public string? SourceFile { get; set; }
}
