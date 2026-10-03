using PackageGuard.Core.Package;

namespace PackageGuard.Core.Scaffolding;

/// <summary>
/// Groups the packages found by a scan into per-license usage counts for <c>packageguard init</c> to display
/// and to build a suggested policy from.
/// </summary>
internal static class LicenseUsageSummarizer
{
    /// <summary>
    /// Groups <paramref name="packages"/> by license, classifying each group and collecting one example
    /// package per group. Results are ordered by package count (descending), then by license name.
    /// </summary>
    public static IReadOnlyList<LicenseUsage> Summarize(IEnumerable<PackageInfo> packages)
    {
        return packages
            .GroupBy(package => package.License ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(ToUsage)
            .OrderByDescending(usage => usage.PackageCount)
            .ThenBy(usage => usage.License, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LicenseUsage ToUsage(IGrouping<string, PackageInfo> group)
    {
        string? license = group.Key.Length == 0 ? null : group.Key;
        PackageInfo example = group.First();

        return new LicenseUsage(license, group.Count(), LicenseClassifier.Classify(license), $"{example.Name} {example.Version}");
    }
}
