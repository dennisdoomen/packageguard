namespace PackageGuard.Core.Init;

/// <summary>
/// Describes how the analyzed software is distributed or operated, which determines how much copyleft
/// exposure <see cref="LicensePresets"/> suggests tolerating.
/// </summary>
public enum SoftwareProfile
{
    /// <summary>
    /// Proprietary or commercial software that is distributed to customers. Only permissive licenses are suggested.
    /// </summary>
    Proprietary,

    /// <summary>
    /// SaaS or hosted software that is never distributed. Permissive and weak-copyleft licenses are suggested,
    /// while strong and network (AGPL) copyleft licenses are excluded because they can trigger on network use.
    /// </summary>
    Saas,

    /// <summary>
    /// Open-source software. Permissive, weak-copyleft, and strong-copyleft licenses are all suggested.
    /// </summary>
    OpenSource
}
