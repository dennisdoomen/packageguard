using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;

namespace PackageGuard.Core.Scaffolding;

/// <summary>
/// Turns a license usage summary and a <see cref="SoftwareProfile"/> into a suggested allow-list policy, and
/// evaluates how many of the scanned packages would violate it.
/// </summary>
public static class PolicyScaffolder
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
    /// Builds the list of licenses to report as warnings: the licenses that <paramref name="profile"/> tolerates
    /// but that still carry copyleft obligations, so they are visible without failing the build.
    /// </summary>
    public static IReadOnlyList<string> BuildWarnLicenses(IEnumerable<LicenseUsage> usages, SoftwareProfile profile)
    {
        return BuildAllowedLicenses(usages, profile)
            .Where(license => LicenseClassifier.IsCopyleft(LicenseClassifier.Classify(license)))
            .ToArray();
    }

    /// <summary>
    /// Counts how many of <paramref name="packages"/> would violate an allow-list policy restricted to
    /// <paramref name="allowedLicenses"/>.
    /// </summary>
    public static int CountViolations(IEnumerable<PackageInfo> packages, IEnumerable<string> allowedLicenses)
    {
        ProjectPolicy policy = CreatePolicy(allowedLicenses, []);

        return packages.Count(package => !policy.AllowList.Allows(package));
    }

    /// <summary>
    /// Counts how many of <paramref name="packages"/> are allowed by <paramref name="allowedLicenses"/> but
    /// would be reported as a warning because their license is in <paramref name="warnLicenses"/>.
    /// </summary>
    public static int CountWarnings(IEnumerable<PackageInfo> packages, IEnumerable<string> allowedLicenses,
        IEnumerable<string> warnLicenses)
    {
        ProjectPolicy policy = CreatePolicy(allowedLicenses, warnLicenses);

        return packages.Count(package => policy.AllowList.Allows(package) && policy.WarnList.Warns(package));
    }

    private static ProjectPolicy CreatePolicy(IEnumerable<string> allowedLicenses, IEnumerable<string> warnLicenses)
    {
        return new ProjectPolicy
        {
            AllowList = new AllowList { Licenses = allowedLicenses.ToList() },
            WarnList = new WarnList { Licenses = warnLicenses.ToList() }
        };
    }
}
