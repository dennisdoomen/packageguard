using NuGet.Versioning;
using PackageGuard.Core.Common;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Policy;

public class DenyList : PackagePolicy
{
    /// <summary>
    /// Gets or sets a value indicating whether to deny all prerelease packages regardless of package name.
    /// </summary>
    public bool Prerelease { get; set; }

    /// <summary>
    /// Determines if the given package is denied by the licenses or packages defined in this deny list.
    /// </summary>
    internal bool Denies(PackageInfo package) => EvaluateDeny(package).IsMatch;

    /// <summary>
    /// Evaluates the given package against this deny list and explains which rule, if any, denied it.
    /// </summary>
    internal PolicyDecision EvaluateDeny(PackageInfo package)
    {
        // Check if prerelease packages are denied
        if (Prerelease && NuGetVersion.Parse(package.Version).IsPrerelease)
        {
            return new PolicyDecision(true, "prerelease packages are denied by policy");
        }

        if (Licenses.Any())
        {
            string? matchingLicense = Licenses.FirstOrDefault(license =>
                license.Equals(package.License, StringComparison.OrdinalIgnoreCase));

            if (matchingLicense is not null)
            {
                LicenseSourceFiles.TryGetValue(matchingLicense, out string? licenseSourceFile);
                return new PolicyDecision(true, $"matches deny list license entry \"{matchingLicense}\"", licenseSourceFile);
            }
        }

        foreach (PackageSelector selector in Packages)
        {
            if (package.Name.MatchesWildcard(selector.Id) &&
                (selector.VersionRange is null || package.SatisfiesRange(package.Name, selector.VersionRange)))
            {
                return new PolicyDecision(true, $"matches deny list package entry \"{selector.Id}\"", selector.SourceFile);
            }
        }

        return new PolicyDecision(false, "no deny list rule matched");
    }
}
