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
                string urlToFetch = ConvertGitHubBlobToRawUrl(package.LicenseUrl);
                string licenseText = await HttpClient.GetStringAsync(urlToFetch);

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
                
                // Fallback for known Microsoft packages if URL fetch fails
                TryAssignKnownMicrosoftLicense(package);
            }
        }
    }

    /// <summary>
    /// Converts GitHub blob URLs to raw URLs for direct license text access.
    /// </summary>
    /// <param name="url">The original URL, potentially a GitHub blob URL</param>
    /// <returns>The converted raw URL if it's a GitHub blob URL, otherwise the original URL</returns>
    private static string ConvertGitHubBlobToRawUrl(string url)
    {
        // Convert GitHub blob URLs to raw URLs
        // Example: https://github.com/dotnet/standard/blob/master/LICENSE.TXT
        // To: https://raw.githubusercontent.com/dotnet/standard/master/LICENSE.TXT
        if (url.Contains("github.com") && url.Contains("/blob/"))
        {
            return url.Replace("github.com", "raw.githubusercontent.com").Replace("/blob/", "/");
        }
        
        return url;
    }

    /// <summary>
    /// Attempts to assign a license for well-known Microsoft packages when URL fetching fails
    /// </summary>
    /// <param name="package">The package to check and potentially assign a license to</param>
    private void TryAssignKnownMicrosoftLicense(PackageInfo package)
    {
        // List of well-known Microsoft packages that should have the Microsoft .NET Library License
        string[] knownMicrosoftPackages = [
            "NETStandard.Library",
            "Microsoft.NETCore.App",
            "Microsoft.AspNetCore.App",
            "Microsoft.WindowsDesktop.App",
            "Microsoft.AspNet.WebApi.Client",
            "Microsoft.AspNet.WebApi.Core",
            "Microsoft.AspNet.WebApi.WebHost",
            "Microsoft.AspNet.WebApi.Owin",
            "Microsoft.AspNet.WebApi.OwinSelfHost"
        ];

        if (knownMicrosoftPackages.Contains(package.Name, StringComparer.OrdinalIgnoreCase))
        {
            package.License = "Microsoft .NET Library License";
            logger.LogInformation("Assigned Microsoft .NET Library License to known Microsoft package {Name}", package.Name);
        }
    }
}
