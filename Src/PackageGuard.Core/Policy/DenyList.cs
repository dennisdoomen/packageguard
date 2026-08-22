using NuGet.Versioning;
using PackageGuard.Core.Common;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Policy;

public class DenyList : PackagePolicy
{
    /// <summary>
    /// Gets or sets a value indicating whether to deny all prerelease packages regardless of package name.
    /// </summary>
    public bool Prerelease { get; set; }

    /// <summary>
    /// Determines if the given package is denied by the licenses or packages defined in this deny list.
    /// </summary>
    internal bool Denies(PackageInfo package)
    {
        // Check if prerelease packages are denied
        if (Prerelease && NuGetVersion.Parse(package.Version).IsPrerelease)
        {
            return true;
        }

        if (Licenses.Any() && Licenses.Contains(package.License!, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (PackageSelector selector in Packages)
        {
            if (package.Name.MatchesWildcard(selector.Id) &&
                (selector.VersionRange is null || package.SatisfiesRange(package.Name, selector.VersionRange)))
            {
                return true;
            }
        }

        return false;
    }
}
