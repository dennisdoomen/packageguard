using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.Common;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.CSharp.FetchingStrategies;

/// <summary>
/// Tries to determine the license by downloading the license file from the URL provided in the package metadata
/// and scanning it for certain words.
/// </summary>
public class UrlLicenseFetcher(ILogger logger) : IFetchLicense
{
    private static readonly HttpClient HttpClient = PackageGuardHttpClient.Create();

    public async Task FetchLicenseAsync(PackageInfo package)
    {
        if (package.LicenseUrl is { Length: > 0 })
        {
            try
            {
                // A GitHub "blob" page is an HTML viewer around the file, so ask for the file itself.
                string url = Regex.Replace(package.LicenseUrl,
                    @"^https://github\.com/([^/]+/[^/]+)/blob/", "https://raw.githubusercontent.com/$1/",
                    RegexOptions.IgnoreCase);

                logger.LogDebug("Fetching license text from {Url}", url);
                string licenseText = await HttpClient.GetStringAsync(url);

                string? license = LicenseTextClassifier.Identify(licenseText);
                if (license is not null)
                {
                    package.License = license;
                    package.LicenseEvidence = LicenseEvidence.Concluded;
                }
                else
                {
                    logger.LogWarning("Did not detect a single, unconditional well-known license in {URL}", package.LicenseUrl);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Failed to extract the license from URL {URL}: {ErrorCode}", package.LicenseUrl, ex.Message);
            }
        }
    }
}
