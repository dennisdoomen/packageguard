namespace PackageGuard.Core.Init;

/// <summary>
/// Maps a <see cref="SoftwareProfile"/> to the license categories it tolerates, and to the CLI-facing preset
/// name used by <c>packageguard init --preset</c>.
/// </summary>
public static class LicensePresets
{
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
    /// Returns the stable, CLI-facing preset name for the given <paramref name="profile"/>.
    /// </summary>
    public static string GetPresetName(SoftwareProfile profile)
    {
        return profile switch
        {
            SoftwareProfile.Proprietary => "permissive-only",
            SoftwareProfile.Saas => "no-network-copyleft",
            SoftwareProfile.OpenSource => "oss-friendly",
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown software profile.")
        };
    }

    /// <summary>
    /// Attempts to parse a CLI-facing preset name (e.g. from <c>--preset</c>) into a <see cref="SoftwareProfile"/>.
    /// </summary>
    public static bool TryParsePresetName(string presetName, out SoftwareProfile profile)
    {
        switch (presetName.Trim().ToLowerInvariant())
        {
            case "permissive-only":
                profile = SoftwareProfile.Proprietary;
                return true;
            case "no-network-copyleft":
                profile = SoftwareProfile.Saas;
                return true;
            case "oss-friendly":
                profile = SoftwareProfile.OpenSource;
                return true;
            default:
                profile = default;
                return false;
        }
    }

    /// <summary>
    /// The CLI-facing preset names, in the order they should be presented to the user.
    /// </summary>
    public static IReadOnlyList<string> PresetNames { get; } = ["permissive-only", "no-network-copyleft", "oss-friendly"];
}
