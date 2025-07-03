using System.Text.RegularExpressions;
using JetBrains.Annotations;
using MemoryPack;
using NuGet.Versioning;
using PackageGuard.Core.Common;

namespace PackageGuard.Core;

[MemoryPackable]
public partial class PackageInfo
{
    private List<string> projects = new();

    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }

    public string[] Projects
    {
        get => projects.ToArray();
        [UsedImplicitly]
        set => projects = new List<string>();
    }

    /// <summary>
    /// Gets or sets the name of the NuGet source that was used to fetch the metadata.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the Url of the NuGet source that was used to fetch the metadata.
    /// </summary>
    public string SourceUrl { get; set; } = "";

    public string? RepositoryUrl { get; set; }

    public bool SatisfiesRange(string name, string? versionRange = null)
    {
        if (!name.Equals(Name, StringComparison.OrdinalIgnoreCase))
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

    public void TrackAsUsedInProject(string projectPath)
    {
        projects.Add(projectPath);
    }

    public override string ToString() => $"{Name}/{Version} ({License})";

    /// <summary>
    /// Returns <c>true</c> if the name or URL of the feed where this package was found matches the given wildcard.
    /// </summary>
    public bool MatchesFeed(string feedWildcard)
    {
        return Source.MatchesWildcard(feedWildcard) || SourceUrl.MatchesWildcard(feedWildcard);
    }
}
