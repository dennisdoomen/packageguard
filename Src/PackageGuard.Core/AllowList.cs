namespace PackageGuard.Core;

public class AllowList : PackagePolicy
{
    internal override bool Complies(PackageInfo package)
    {
        bool licenseComplies = !(Licenses.Any() && !Licenses.Contains(package.License!, StringComparer.OrdinalIgnoreCase));

        bool packageComplies = true;

        foreach (PackageSelector selector in Packages)
        {
            if (package.Id == selector.Id)
            {
                if (selector.VersionRange is not null &&
                    !package.SatisfiesRange(selector.Id, selector.VersionRange))
                {
                    packageComplies = false;
                }
                else
                {
                    // If the package (and version) is allowlisted, we don't care about the license violation
                    licenseComplies = true;
                }
                break;
            }
        }

        return licenseComplies && packageComplies;
    }
}
