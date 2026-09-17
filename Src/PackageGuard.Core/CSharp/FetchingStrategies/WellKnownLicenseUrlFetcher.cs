using System.Text.RegularExpressions;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.CSharp.FetchingStrategies;

/// <summary>
/// Recognizes license URLs that already unambiguously identify an SPDX license, so the license text
/// doesn't need to be downloaded and scanned.
/// </summary>
internal class WellKnownLicenseUrlFetcher : IFetchLicense
{
    /// <summary>
    /// Matches the URLs NuGet itself generates for packages that declare a license expression, e.g.
    /// <c>https://licenses.nuget.org/MIT</c>. The SPDX id is the path segment.
    /// </summary>
    private static readonly Regex NuGetGeneratedLicenseUrl =
        new(@"^https?://(www\.)?licenses\.nuget\.org/(?<id>[^/?#]+)/?$", RegexOptions.IgnoreCase);

    /// <summary>
    /// A small, curated set of well-known third-party license URLs that map unambiguously to an SPDX id.
    /// </summary>
    private static readonly (Regex Pattern, string LicenseId)[] WellKnownUrlPatterns =
    [
        (new Regex(@"^https?://(www\.)?apache\.org/licenses/LICENSE-2\.0(\.(txt|html))?/?$", RegexOptions.IgnoreCase), "Apache-2.0"),
        (new Regex(@"^https?://(www\.)?opensource\.org/licenses?/MIT/?$", RegexOptions.IgnoreCase), "MIT"),
        (new Regex(@"^https?://(www\.)?opensource\.org/licenses?/BSD-2-Clause/?$", RegexOptions.IgnoreCase), "BSD-2-Clause"),
        (new Regex(@"^https?://(www\.)?opensource\.org/licenses?/BSD-3-Clause/?$", RegexOptions.IgnoreCase), "BSD-3-Clause"),
        (new Regex(@"^https?://(www\.)?opensource\.org/licenses?/ISC/?$", RegexOptions.IgnoreCase), "ISC"),
        (new Regex(@"^https?://(www\.)?gnu\.org/licenses/gpl-2\.0(\.[a-z]+)?/?$", RegexOptions.IgnoreCase), "GPL-2.0"),
        (new Regex(@"^https?://(www\.)?gnu\.org/licenses/gpl-3\.0(\.[a-z]+)?/?$", RegexOptions.IgnoreCase), "GPL-3.0"),
        (new Regex(@"^https?://(www\.)?gnu\.org/licenses/lgpl-2\.1(\.[a-z]+)?/?$", RegexOptions.IgnoreCase), "LGPL-2.1"),
        (new Regex(@"^https?://(www\.)?gnu\.org/licenses/lgpl-3\.0(\.[a-z]+)?/?$", RegexOptions.IgnoreCase), "LGPL-3.0"),
        (new Regex(@"^https?://(www\.)?mozilla\.org/(en-US/)?MPL/2\.0/?$", RegexOptions.IgnoreCase), "MPL-2.0"),
        (new Regex(@"^https?://(www\.)?unlicense\.org/?$", RegexOptions.IgnoreCase), "Unlicense")
    ];

    public Task FetchLicenseAsync(PackageInfo package)
    {
        if (package.LicenseUrl is { Length: > 0 } url)
        {
            Match nuGetMatch = NuGetGeneratedLicenseUrl.Match(url);
            if (nuGetMatch.Success)
            {
                package.License = Uri.UnescapeDataString(nuGetMatch.Groups["id"].Value);
                package.LicenseEvidence = LicenseEvidence.Concluded;
                return Task.CompletedTask;
            }

            foreach ((Regex pattern, string licenseId) in WellKnownUrlPatterns)
            {
                if (pattern.IsMatch(url))
                {
                    package.License = licenseId;
                    package.LicenseEvidence = LicenseEvidence.Concluded;
                    break;
                }
            }
        }

        return Task.CompletedTask;
    }
}
