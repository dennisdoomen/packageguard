using NuGet.Versioning;

namespace PackageGuard.Core;

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
            if (package.Name == selector.Id &&
                (selector.VersionRange is null || package.SatisfiesRange(selector.Id, selector.VersionRange)))
            {
                return true;
            }
        }

        return false;
    }
}
