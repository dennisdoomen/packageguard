using PackageGuard.Core.Policy;

namespace PackageGuard.Core.Scaffolding;

/// <summary>
/// Classifies SPDX license identifiers by the copyleft obligations they impose, so <c>packageguard init</c>
/// can flag copyleft licenses and suggest a policy that matches the project's <see cref="SoftwareProfile"/>.
/// </summary>
internal static class LicenseClassifier
{
    /// <summary>
    /// Known license identifiers mapped to their <see cref="LicenseCategory"/>. Any license not listed here,
    /// including a missing/unresolved license, is classified as <see cref="LicenseCategory.Unknown"/> so it
    /// never gets silently allowed by a suggested policy.
    /// </summary>
    private static readonly Dictionary<string, LicenseCategory> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MIT"] = LicenseCategory.Permissive,
        ["MIT-0"] = LicenseCategory.Permissive,
        ["Apache-2.0"] = LicenseCategory.Permissive,
        ["BSD-2-Clause"] = LicenseCategory.Permissive,
        ["BSD-3-Clause"] = LicenseCategory.Permissive,
        ["BSD-3-Clause-Clear"] = LicenseCategory.Permissive,
        ["0BSD"] = LicenseCategory.Permissive,
        ["ISC"] = LicenseCategory.Permissive,
        ["Unlicense"] = LicenseCategory.Permissive,
        ["CC0-1.0"] = LicenseCategory.Permissive,
        ["Zlib"] = LicenseCategory.Permissive,
        ["BSL-1.0"] = LicenseCategory.Permissive,
        ["Python-2.0"] = LicenseCategory.Permissive,
        ["PSF-2.0"] = LicenseCategory.Permissive,
        ["MS-PL"] = LicenseCategory.Permissive,
        ["Microsoft .NET Library License"] = LicenseCategory.Permissive,
        ["LGPL-2.1-only"] = LicenseCategory.WeakCopyleft,
        ["LGPL-2.1-or-later"] = LicenseCategory.WeakCopyleft,
        ["LGPL-3.0-only"] = LicenseCategory.WeakCopyleft,
        ["LGPL-3.0-or-later"] = LicenseCategory.WeakCopyleft,
        ["MPL-1.1"] = LicenseCategory.WeakCopyleft,
        ["MPL-2.0"] = LicenseCategory.WeakCopyleft,
        ["EPL-1.0"] = LicenseCategory.WeakCopyleft,
        ["EPL-2.0"] = LicenseCategory.WeakCopyleft,
        ["CDDL-1.0"] = LicenseCategory.WeakCopyleft,
        ["CDDL-1.1"] = LicenseCategory.WeakCopyleft,
        ["MS-RL"] = LicenseCategory.WeakCopyleft,
        ["GPL-2.0-only"] = LicenseCategory.StrongCopyleft,
        ["GPL-2.0-or-later"] = LicenseCategory.StrongCopyleft,
        ["GPL-3.0-only"] = LicenseCategory.StrongCopyleft,
        ["GPL-3.0-or-later"] = LicenseCategory.StrongCopyleft,
        ["AGPL-3.0-only"] = LicenseCategory.NetworkCopyleft,
        ["AGPL-3.0-or-later"] = LicenseCategory.NetworkCopyleft
    };

    /// <summary>
    /// Classifies the given SPDX license expression. Returns <see cref="LicenseCategory.Unknown"/> for a
    /// missing, empty, or unrecognized license. For <c>OR</c> the least restrictive option counts, since the
    /// consumer can choose; for <c>AND</c> the most restrictive one does.
    /// </summary>
    public static LicenseCategory Classify(string? license)
    {
        if (string.IsNullOrWhiteSpace(license))
        {
            return LicenseCategory.Unknown;
        }

        return LicenseExpression.Parse(license).Evaluate(
            term => Categories.GetValueOrDefault(term.Id, LicenseCategory.Unknown),
            (left, right) => Restrictiveness(left) <= Restrictiveness(right) ? left : right,
            (left, right) => Restrictiveness(left) >= Restrictiveness(right) ? left : right);
    }

    /// <summary>
    /// Ranks the categories from least to most restrictive, treating an unknown license as the most restrictive.
    /// </summary>
    private static int Restrictiveness(LicenseCategory category) =>
        category == LicenseCategory.Unknown ? int.MaxValue : (int)category;

    /// <summary>
    /// Returns <c>true</c> for any category that imposes copyleft obligations of some kind.
    /// </summary>
    public static bool IsCopyleft(LicenseCategory category) =>
        category is LicenseCategory.WeakCopyleft or LicenseCategory.StrongCopyleft or LicenseCategory.NetworkCopyleft;
}
