namespace PackageGuard.Core;

public class DenyList : PackagePolicy
{
    internal override bool Complies(PackageInfo package)
    {
        if (Licenses.Any() && Licenses.Contains(package.License!, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (PackageSelector selector in Packages)
        {
            if (package.Id == selector.Id &&
                (selector.VersionRange is null || package.SatisfiesRange(selector.Id, selector.VersionRange)))
            {
                return false;
            }
        }

        return true;
    }
}
