namespace PackageGuard.Core.Init;

/// <summary>
/// Classifies a license by the copyleft obligations it imposes, used by <see cref="LicenseClassifier"/> and
/// <see cref="LicensePresets"/> to decide which licenses fit a given <see cref="SoftwareProfile"/>.
/// </summary>
public enum LicenseCategory
{
    /// <summary>
    /// The license could not be resolved, or isn't recognized. Never included in a suggested allow list.
    /// </summary>
    Unknown,

    /// <summary>
    /// A permissive license (e.g. MIT, Apache-2.0, BSD) that imposes no copyleft obligations.
    /// </summary>
    Permissive,

    /// <summary>
    /// A weak (file/library-level) copyleft license (e.g. LGPL, MPL) whose obligations are limited to the
    /// licensed component itself.
    /// </summary>
    WeakCopyleft,

    /// <summary>
    /// A strong copyleft license (e.g. GPL) that can require derivative works to be released under the same license.
    /// </summary>
    StrongCopyleft,

    /// <summary>
    /// A network (AGPL-style) copyleft license, whose obligations extend to software offered as a network service.
    /// </summary>
    NetworkCopyleft
}
