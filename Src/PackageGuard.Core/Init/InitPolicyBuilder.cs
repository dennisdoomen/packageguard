using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;

namespace PackageGuard.Core.Init;

/// <summary>
/// Turns a license usage summary and a <see cref="SoftwareProfile"/> into a suggested allow-list policy, and
/// evaluates how many of the scanned packages would violate it.
/// </summary>
public static class InitPolicyBuilder
{
    /// <summary>
    /// Builds the list of licenses to allow for <paramref name="profile"/>, limited to the licenses that were
    /// actually found by the scan. A license with no resolved value is never included, regardless of profile.
    /// </summary>
    public static IReadOnlyList<string> BuildAllowedLicenses(IEnumerable<LicenseUsage> usages, SoftwareProfile profile)
    {
        IReadOnlySet<LicenseCategory> allowedCategories = LicensePresets.GetAllowedCategories(profile);

        return usages
            .Where(usage => usage.License is not null && allowedCategories.Contains(usage.Category))
            .Select(usage => usage.License!)
            .OrderBy(license => license, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Counts how many of <paramref name="packages"/> would violate an allow-list policy restricted to
    /// <paramref name="allowedLicenses"/>.
    /// </summary>
    public static int CountViolations(IEnumerable<PackageInfo> packages, IEnumerable<string> allowedLicenses)
    {
        var policy = new ProjectPolicy
        {
            AllowList = new AllowList { Licenses = allowedLicenses.ToList() }
        };

        return packages.Count(package => !policy.AllowList.Allows(package));
    }
}
