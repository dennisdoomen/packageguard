using NuGet.Versioning;
using PackageGuard.Core.Common;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Policy;

/// <summary>
/// A list of packages and licenses that, when matched, are reported as warnings instead of failing the build.
/// A match against <see cref="DenyList"/> always takes precedence over a match here.
/// </summary>
public class WarnList : PackagePolicy
{
    /// <summary>
    /// Gets or sets a value indicating whether to flag all prerelease packages regardless of package name.
    /// </summary>
    public bool Prerelease { get; set; }

    /// <summary>
    /// Determines if the given package matches the licenses or packages defined in this warn list.
    /// </summary>
    internal bool Warns(PackageInfo package) => EvaluateWarn(package).IsMatch;

    /// <summary>
    /// Evaluates the given package against this warn list and explains which rule, if any, matched it.
    /// </summary>
    internal PolicyDecision EvaluateWarn(PackageInfo package)
    {
        if (Prerelease && NuGetVersion.Parse(package.Version).IsPrerelease)
        {
            return new PolicyDecision
            {
                IsMatch = true,
                Reason = "prerelease packages are flagged for warning by policy"
            };
        }

        IReadOnlyList<string> matchingLicenses = LicenseExpression.FindRestrictingEntries(package.License, Licenses);
        if (matchingLicenses.Count > 0)
        {
            return new PolicyDecision
            {
                IsMatch = true,
                Reason = LicenseExpression.DescribeMatch("warn list", matchingLicenses, package.License),
                SourceFile = FindLicenseSourceFiles(matchingLicenses)
            };
        }

        foreach (PackageSelector selector in Packages)
        {
            if (package.Name.MatchesWildcard(selector.Id) &&
                (selector.VersionRange is null || package.SatisfiesRange(package.Name, selector.VersionRange)))
            {
                return new PolicyDecision
                {
                    IsMatch = true,
                    Reason = $"matches warn list package entry \"{selector.Id}\"",
                    SourceFile = selector.SourceFile
                };
            }
        }

        return new PolicyDecision
        {
            IsMatch = false,
            Reason = "no warn list rule matched"
        };
    }
}
