using System.Text.RegularExpressions;

namespace PackageGuard.Core.Package;

/// <summary>
/// Recognizes license URLs that are already known to unambiguously identify an SPDX license, so callers
/// don't need to download the license text or probe the URL over the network.
/// </summary>
internal static class WellKnownLicenseUrls
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

    /// <summary>
    /// Resolves <paramref name="url"/> to the SPDX id it unambiguously identifies, when it is a recognized
    /// NuGet-generated or well-known third-party license URL.
    /// </summary>
    public static bool TryGetLicenseId(string url, out string? licenseId)
    {
        Match nuGetMatch = NuGetGeneratedLicenseUrl.Match(url);
        if (nuGetMatch.Success)
        {
            licenseId = Uri.UnescapeDataString(nuGetMatch.Groups["id"].Value);
            return true;
        }

        foreach ((Regex pattern, string id) in WellKnownUrlPatterns)
        {
            if (pattern.IsMatch(url))
            {
                licenseId = id;
                return true;
            }
        }

        licenseId = null;
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="url"/> is a recognized NuGet-generated or
    /// well-known third-party license URL, and therefore already known to be canonical and reachable.
    /// </summary>
    public static bool IsWellKnown(string url) => TryGetLicenseId(url, out _);
}
