namespace PackageGuard.Core.Scaffolding;

/// <summary>
/// Maps a <see cref="SoftwareProfile"/> to the license categories it tolerates, and to the CLI-facing preset
/// name used by <c>packageguard init --preset</c>.
/// </summary>
internal static class LicensePresets
{
    private static readonly Dictionary<string, SoftwareProfile> PresetsByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["permissive-only"] = SoftwareProfile.Proprietary,
        ["no-network-copyleft"] = SoftwareProfile.Saas,
        ["oss-friendly"] = SoftwareProfile.OpenSource
    };
    /// <summary>
    /// Returns the license categories that are considered acceptable for the given <paramref name="profile"/>.
    /// </summary>
    public static IReadOnlySet<LicenseCategory> GetAllowedCategories(SoftwareProfile profile)
    {
        return profile switch
        {
            SoftwareProfile.Proprietary => new HashSet<LicenseCategory> { LicenseCategory.Permissive },
            SoftwareProfile.Saas => new HashSet<LicenseCategory> { LicenseCategory.Permissive, LicenseCategory.WeakCopyleft },
            SoftwareProfile.OpenSource => new HashSet<LicenseCategory>
            {
                LicenseCategory.Permissive, LicenseCategory.WeakCopyleft, LicenseCategory.StrongCopyleft, LicenseCategory.NetworkCopyleft
            },
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown software profile.")
        };
    }

    /// <summary>
    /// The CLI-facing preset names, in the order they should be presented to the user.
    /// </summary>
    public static IReadOnlyList<string> PresetNames { get; } = [.. PresetsByName.Keys];

    /// <summary>
    /// Returns the stable, CLI-facing preset name for the given <paramref name="profile"/>.
    /// </summary>
    public static string GetPresetName(SoftwareProfile profile)
    {
        return PresetsByName.FirstOrDefault(preset => preset.Value == profile).Key
               ?? throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown software profile.");
    }

    /// <summary>
    /// Attempts to parse a CLI-facing preset name (e.g. from <c>--preset</c>) into a <see cref="SoftwareProfile"/>.
    /// </summary>
    public static bool TryParsePresetName(string presetName, out SoftwareProfile profile)
    {
        return PresetsByName.TryGetValue(presetName.Trim(), out profile);
    }
}