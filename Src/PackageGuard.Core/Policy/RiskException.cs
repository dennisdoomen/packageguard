using PackageGuard.Core.Common;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Policy;

/// <summary>
/// An explicit, documented exception that keeps a package (optionally pinned to a version or version range)
/// from being denied by <see cref="DenyList"/>'s risk-based rules, even if it exceeds a threshold or has a
/// known vulnerability.
/// </summary>
public class RiskException
{
    /// <summary>
    /// The package name, or wildcard, this exception applies to.
    /// </summary>
    public string Package { get; set; } = "";

    /// <summary>
    /// An optional NuGet version range this exception is limited to. Applies to every version when omitted.
    /// </summary>
    public string? Versions { get; set; }

    /// <summary>
    /// A human-readable explanation for why this package is excluded from risk-based denial.
    /// </summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// An optional date after which this exception no longer applies.
    /// </summary>
    public DateOnly? ExpiresOn { get; set; }

    /// <summary>
    /// Gets or sets the configuration file this exception was loaded from, when known.
    /// </summary>
    public string? SourceFile { get; set; }

    /// <summary>
    /// Determines whether this exception currently applies to the given package.
    /// </summary>
    internal bool Matches(PackageInfo package)
    {
        if (ExpiresOn is { } expiresOn && DateOnly.FromDateTime(DateTime.UtcNow) > expiresOn)
        {
            return false;
        }

        return package.Name.MatchesWildcard(Package) &&
               (Versions is null || package.SatisfiesRange(package.Name, Versions));
    }
}
