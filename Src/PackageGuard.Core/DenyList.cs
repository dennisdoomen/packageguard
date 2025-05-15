namespace PackageGuard.Core;

public class DenyList : PackagePolicy
{
    /// <summary>
    /// Determines if the given package is denied by the licenses or packages defined in this deny list.
    /// </summary>
    internal bool Denies(PackageInfo package)
    {
        if (Licenses.Any() && Licenses.Contains(package.License!, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (PackageSelector selector in Packages)
        {
            if (package.Id == selector.Id &&
                (selector.VersionRange is null || package.SatisfiesRange(selector.Id, selector.VersionRange)))
            {
                return true;
            }
        }

        return false;
    }
}
