using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

internal sealed class LicenseUrlRiskEnricher(ILogger logger) : IEnrichPackageRisk
{
    private static readonly HttpClient HttpClient = new();

    public async Task EnrichAsync(PackageInfo package)
    {
        if (string.IsNullOrWhiteSpace(package.LicenseUrl))
        {
            package.HasValidLicenseUrl = false;
            return;
        }

        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(package.LicenseUrl,
                HttpCompletionOption.ResponseHeadersRead);

            package.HasValidLicenseUrl = response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogDebug("Failed to validate license URL for {Name} {Version}: {Error}",
                package.Name, package.Version, ex.Message);
            package.HasValidLicenseUrl = false;
        }
    }
}
