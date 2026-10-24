using NuGet.Versioning;
using PackageGuard.Core.Common;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Policy;

public class AllowList : PackagePolicy
{
    /// <summary>
    /// One or more NuGet feeds which packages are allowed regardless of version of license.
    /// </summary>
    /// <value>
    /// Each feed is wildcard string that can match the NuGet feed name or URL.
    /// </value>
    public List<string> Feeds { get; set; } = [];

    /// <summary>
    /// Gets or sets the configuration file each entry in <see cref="Feeds"/> was first introduced by,
    /// keyed by the feed wildcard string. Populated by <c>ConfigurationLoader</c>; empty when unknown.
    /// </summary>
    public Dictionary<string, string> FeedSourceFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a value indicating whether to allow prerelease packages regardless of package name.
    /// </summary>
    public bool Prerelease { get; set; } = true;

    /// <summary>
    /// Replaces the hint for a package whose license is not in <see cref="PackagePolicy.Licenses"/>. Used by an
    /// allow list that does not come from a configuration file, so the hint cannot point to one.
    /// </summary>
    internal string? LicenseResolution { get; init; }

    /// <summary>
    /// Verifies if the given package complies given the feeds, packages and licenses defined in this allow list.
    /// </summary>
    /// <returns>Returns <c>true</c> if the package is allowed according to the policies in this list.</returns>
    internal bool Allows(PackageInfo package) => EvaluateAllow(package).IsMatch;

    /// <summary>
    /// Evaluates the given package against this allow list and explains which rule, if any, decided the outcome.
    /// </summary>
    internal PolicyDecision EvaluateAllow(PackageInfo package)
    {
        string? matchingFeed = Feeds.FirstOrDefault(package.MatchesFeed);
        if (matchingFeed is not null)
        {
            FeedSourceFiles.TryGetValue(matchingFeed, out string? feedSourceFile);
            return new PolicyDecision
            {
                IsMatch = true,
                Reason = $"matches allow list feed entry \"{matchingFeed}\"",
                SourceFile = feedSourceFile
            };
        }

        // Check if prerelease packages are explicitly disallowed
        bool prereleaseComplies = Prerelease || !NuGetVersion.Parse(package.Version).IsPrerelease;
        if (!prereleaseComplies)
        {
            return new PolicyDecision
            {
                IsMatch = false,
                Reason = "prerelease packages are not allowed by policy",
                Resolution = "Use a stable version, or set prerelease to true in the allow section of your config"
            };
        }

        bool licenseComplies = !(Licenses.Any() && !Licenses.Contains(package.License!, StringComparer.OrdinalIgnoreCase));

        foreach (PackageSelector selector in Packages)
        {
            if (package.Name.MatchesWildcard(selector.Id))
            {
                if (selector.VersionRange is not null &&
                    !package.SatisfiesRange(package.Name, selector.VersionRange))
                {
                    return new PolicyDecision
                    {
                        IsMatch = false,
                        Reason = $"matches allow list package entry \"{selector.Id}\" but version {package.Version} does not satisfy \"{selector.VersionRange}\"",
                        SourceFile = selector.SourceFile,
                        Resolution = $"Use a version that satisfies \"{selector.VersionRange}\", or change that range in the allow " +
                                     $"section of {DescribeConfigurationFile(selector.SourceFile)}"
                    };
                }

                // If the package (and version) is allowed, we don't care about the license violation
                return new PolicyDecision
                {
                    IsMatch = true,
                    Reason = $"matches allow list package entry \"{selector.Id}\"",
                    SourceFile = selector.SourceFile
                };
            }
        }

        if (!licenseComplies)
        {
            return new PolicyDecision
            {
                IsMatch = false,
                Reason = $"license \"{package.License}\" is not in the allow list",
                Resolution = LicenseResolution ??
                             "Add the license or the package to the allow section of your config, or use a different package. " +
                             "Note that riskExceptions do not exempt a package from license rules"
            };
        }

        if (Licenses.Any())
        {
            string? matchingLicense = Licenses.FirstOrDefault(license =>
                license.Equals(package.License, StringComparison.OrdinalIgnoreCase));

            if (matchingLicense is not null)
            {
                LicenseSourceFiles.TryGetValue(matchingLicense, out string? licenseSourceFile);
                return new PolicyDecision
                {
                    IsMatch = true,
                    Reason = $"matches allow list license entry \"{matchingLicense}\"",
                    SourceFile = licenseSourceFile
                };
            }
        }

        return new PolicyDecision
        {
            IsMatch = true,
            Reason = "no allow list rule matched; no allow list restrictions apply"
        };
    }
}
