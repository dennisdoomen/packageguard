using Microsoft.Extensions.Logging;

namespace PackageGuard.Core.FetchingStrategies;

/// <summary>
/// Fetches licenses using a license URL.
/// </summary>
public class UrlLicenseFetcher(ILogger logger) : IFetchLicense
{
    private static readonly HttpClient HttpClient = new();

    public async Task FetchLicenseAsync(PackageInfo package)
    {
        package.License = "Unknown";

        if (package.LicenseUrl is { Length: > 0 })
        {
            try
            {
                string licenseText = await HttpClient.GetStringAsync(package.LicenseUrl);

                if (licenseText.Contains("MIT license", StringComparison.OrdinalIgnoreCase))
                {
                    package.License = "MIT";
                }
                else if (licenseText.Contains("Apache License", StringComparison.OrdinalIgnoreCase))
                {
                    package.License = "Apache-2.0";
                }
                else if (licenseText.Contains("GNU General Public License", StringComparison.OrdinalIgnoreCase))
                {
                    package.License = "GPL-3.0";
                }
                else if (licenseText.Contains("MICROSOFT SOFTWARE LICENSE TERMS", StringComparison.OrdinalIgnoreCase))
                {
                    package.License = "Microsoft .NET Library License";
                }
                else
                {
                    logger.LogWarning("Did not detect any well-known licenses in {URL}", package.LicenseUrl);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Failed to extract the license from URL {URL}: {ErrorCode}", package.LicenseUrl, ex.Message);
            }
        }
    }
}
