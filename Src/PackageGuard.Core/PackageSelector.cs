namespace PackageGuard.Core;

public class PackageSelector(string id)
{
    public PackageSelector(string id, string versionRange) : this(id)
    {
        VersionRange = versionRange;
    }

    /// <summary>
    /// The ID of the package, e.g. Newtonsoft.Json
    /// </summary>
    public string Id { get; set; } = id;

    /// <summary>
    /// Defines a version or version range using NuGet versioning format, e.g.
    /// </summary>
    public string? VersionRange { get; set; }
}
