using NuGet.Versioning;

namespace PackageGuard.Core;

public class AllowList : PackagePolicy
{
    /// <summary>
    /// One or more NuGet feeds which packages are allowed regardless of version of license.
    /// </summary>
    /// <value>
    /// Each feed is wildcard string that can match the NuGet feed name or URL.
    /// </value>
    public List<string> Feeds { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether to allow prerelease packages regardless of package name.
    /// </summary>
    public bool Prerelease { get; set; } = true;

    /// <summary>
    /// Verifies if the given package complies given the feeds, packages and licenses defined in this allow list.
    /// </summary>
    /// <returns>Returns <c>true</c> if the package is allowed according to the policies in this list.</returns>
    internal bool Allows(PackageInfo package)
    {
        if (PackageIsExplicitlyAllowedByFeed(package))
        {
            return true;
        }

        // Check if prerelease packages are explicitly disallowed
        bool prereleaseComplies = Prerelease || !NuGetVersion.Parse(package.Version).IsPrerelease;

        bool licenseComplies = !(Licenses.Any() && !Licenses.Contains(package.License!, StringComparer.OrdinalIgnoreCase));

        bool packageComplies = true;

        foreach (PackageSelector selector in Packages)
        {
            if (package.Name == selector.Id)
            {
                if (selector.VersionRange is not null &&
                    !package.SatisfiesRange(selector.Id, selector.VersionRange))
                {
                    packageComplies = false;
                }
                else
                {
                    // If the package (and version) is allowed, we don't care about the license violation
                    licenseComplies = true;
                }

                break;
            }
        }

        return prereleaseComplies && licenseComplies && packageComplies;
    }

    private bool PackageIsExplicitlyAllowedByFeed(PackageInfo package) => Feeds.Any(package.MatchesFeed);
}
