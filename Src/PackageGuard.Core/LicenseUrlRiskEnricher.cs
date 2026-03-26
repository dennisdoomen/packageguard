using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

/// <summary>
/// Enriches package risk information by validating that the package's license URL is reachable via HTTP.
/// </summary>
internal sealed class LicenseUrlRiskEnricher(ILogger logger) : IEnrichPackageRisk
{
    /// <summary>
    /// Shared HTTP client used to validate license URLs.
    /// </summary>
    private static readonly HttpClient HttpClient = new();

    /// <summary>
    /// Returns <see langword="true"/> when the license URL for <paramref name="package"/> has already been validated.
    /// </summary>
    public bool HasCachedData(PackageInfo package) => package.HasValidatedLicenseUrl;

    /// <summary>
    /// Validates the license URL of <paramref name="package"/> by performing an HTTP request and stores the result
    /// in <see cref="PackageInfo.HasValidLicenseUrl"/>.
    /// </summary>
    public async Task EnrichAsync(PackageInfo package)
    {
        if (string.IsNullOrWhiteSpace(package.LicenseUrl))
        {
            package.HasValidLicenseUrl = false;
            package.HasValidatedLicenseUrl = true;
            return;
        }

        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(package.LicenseUrl,
                HttpCompletionOption.ResponseHeadersRead);

            package.HasValidLicenseUrl = response.IsSuccessStatusCode;
            package.HasValidatedLicenseUrl = true;
        }
        catch (Exception ex)
        {
            logger.LogDebug("Failed to validate license URL for {Name} {Version}: {Error}",
                package.Name, package.Version, ex.Message);
            package.HasValidLicenseUrl = false;
            package.HasValidatedLicenseUrl = true;
        }
    }
}
