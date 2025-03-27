using NuGet.Versioning;

namespace PackageGuard.Core;

public class PackageInfo
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }

    public List<string> Projects { get; set; } = new();

    /// <summary>
    /// Gets or sets the source URL of the NuGet source that was used to fetch the metadata.
    /// </summary>
    public string? Source { get; set; }

    public string? RepositoryUrl { get; set; }

    public bool SatisfiesRange(string name, string? versionRange = null)
    {
        if (!name.Equals(Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (versionRange is null)
        {
            return true;
        }

        var range = VersionRange.Parse(versionRange);
        return range.Satisfies(NuGetVersion.Parse(Version));
    }

    public void Add(string projectPath)
    {
        Projects.Add(projectPath);
    }

    public override string ToString() => $"{Id}/{Version} ({License})";
}
