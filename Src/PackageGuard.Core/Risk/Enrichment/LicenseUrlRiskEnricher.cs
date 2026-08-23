using Microsoft.Extensions.Logging;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Risk.Enrichment;

/// <summary>
/// Enriches package risk information by validating that the package's license URL is reachable via HTTP.
/// </summary>
internal sealed class LicenseUrlRiskEnricher(ILogger logger, HttpClient? httpClient = null) : IEnrichPackageRisk
{
    /// <summary>
    /// Shared HTTP client used to validate license URLs.
    /// </summary>
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// The client this enricher sends its requests through.
    /// </summary>
    private readonly HttpClient httpClient = httpClient ?? SharedHttpClient;

    /// <summary>
    /// The outcome per license URL, so that the many packages sharing one URL only cost a single request.
    /// </summary>
    private static readonly Dictionary<string, bool> ResultsByUrl = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Guards access to <see cref="ResultsByUrl"/>.
    /// </summary>
    private static readonly Lock ResultsLock = new();

    /// <summary>
    /// Returns <see langword="true"/> when the license URL for <paramref name="package"/> has already been validated.
    /// </summary>
    public bool HasCachedData(PackageInfo package) => package.HasValidatedLicenseUrl;

    /// <summary>
    /// Validates the license URL of <paramref name="package"/> and stores the result in
    /// <see cref="PackageInfo.HasValidLicenseUrl"/>.
    /// </summary>
    public async Task EnrichAsync(PackageInfo package)
    {
        if (string.IsNullOrWhiteSpace(package.LicenseUrl))
        {
            Apply(package, isValid: false);
            return;
        }

        bool? known = FindResult(package.LicenseUrl);
        Apply(package, known ?? await ValidateAsync(package.LicenseUrl));
    }

    /// <summary>
    /// Returns the outcome already established for a license URL, or <see langword="null"/> when it is new.
    /// </summary>
    private static bool? FindResult(string licenseUrl)
    {
        lock (ResultsLock)
        {
            return ResultsByUrl.TryGetValue(licenseUrl, out bool isValid) ? isValid : null;
        }
    }

    /// <summary>
    /// Checks whether a license URL is reachable, remembering the answer for the packages that share it.
    /// </summary>
    /// <remarks>
    /// Whole families of packages point at the same license, so the answer is worth caching. The check asks for the
    /// headers only: whether the document is reachable is all that matters, and its body can be sizeable.
    /// </remarks>
    private async Task<bool> ValidateAsync(string licenseUrl)
    {
        bool isValid = await SendHeadRequestAsync(licenseUrl);

        lock (ResultsLock)
        {
            ResultsByUrl[licenseUrl] = isValid;
        }

        return isValid;
    }

    /// <summary>
    /// Sends a <c>HEAD</c> request, falling back to <c>GET</c> for the servers that reject it.
    /// </summary>
    private async Task<bool> SendHeadRequestAsync(string licenseUrl)
    {
        try
        {
            logger.LogDebug("Validating license URL {Url}", licenseUrl);
            using HttpRequestMessage request = new(HttpMethod.Head, licenseUrl);
            using HttpResponseMessage response =
                await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            return response.IsSuccessStatusCode || await IsReachableWithGetAsync(licenseUrl, response);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to validate license URL {LicenseUrl}", licenseUrl);
            return false;
        }
    }

    /// <summary>
    /// Retries with a <c>GET</c> when the server does not allow <c>HEAD</c>, which some hosts signal with
    /// <c>405</c> or <c>501</c>.
    /// </summary>
    private async Task<bool> IsReachableWithGetAsync(string licenseUrl, HttpResponseMessage headResponse)
    {
        if (headResponse.StatusCode is not (System.Net.HttpStatusCode.MethodNotAllowed or
            System.Net.HttpStatusCode.NotImplemented))
        {
            return false;
        }

        using HttpResponseMessage response =
            await httpClient.GetAsync(licenseUrl, HttpCompletionOption.ResponseHeadersRead);

        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Records the validation outcome on the package.
    /// </summary>
    private static void Apply(PackageInfo package, bool isValid)
    {
        package.HasValidLicenseUrl = isValid;
        package.HasValidatedLicenseUrl = true;
    }

    /// <summary>
    /// Discards the cached license URL outcomes. Only used by the tests.
    /// </summary>
    internal static void ClearCache()
    {
        lock (ResultsLock)
        {
            ResultsByUrl.Clear();
        }
    }
}
