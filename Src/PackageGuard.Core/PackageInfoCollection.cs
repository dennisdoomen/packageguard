using System.Collections;

namespace PackageGuard.Core;

public class PackageInfoCollection : IEnumerable<PackageInfo>
{
    private readonly HashSet<PackageInfo> packages = new();

    public IEnumerator<PackageInfo> GetEnumerator() => packages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)packages).GetEnumerator();

    public void Add(PackageInfo package)
    {
        packages.Add(package);
        UpdateLicenseForWellKnownLicenseUrls(package);
    }

    private void UpdateLicenseForWellKnownLicenseUrls(PackageInfo package)
    {
        if (package.License is null && package.LicenseUrl is not null)
        {
            package.License = packages.FirstOrDefault(x => x.LicenseUrl == package.LicenseUrl)?.License;
        }
    }

    public PackageInfo? Find(string libraryName, string libraryVersion)
    {
        return packages.FirstOrDefault(p => p?.Id == libraryName && p?.Version == libraryVersion);
    }
}
