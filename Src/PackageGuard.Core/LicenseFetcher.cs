using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.CSharp.FetchingStrategies;

namespace PackageGuard.Core;

/// <summary>
/// Responsible for fetching and amending missing license information for a package.
/// </summary>
public sealed class LicenseFetcher(ILogger logger, string? gitHubApiKey = null)
{
    private readonly IReadOnlyList<IFetchLicense> fetchers =
        [
            new CorrectMisbehavingPackagesFetcher(),
            new GitHubLicenseFetcher(gitHubApiKey),
            new UrlLicenseFetcher(logger)
        ];

    internal LicenseFetcher(ILogger logger, string? gitHubApiKey, IEnumerable<IFetchLicense> fetchers) : this(logger, gitHubApiKey)
    {
        this.fetchers = fetchers.ToArray();
    }

    /// <summary>
    /// Enriches the package with license information by trying the configured fetch strategies in order.
    /// </summary>
    public async Task AmendWithMissingLicenseInformation(PackageInfo package)
    {
        LogFetchConfiguration();

        if (package.License is null)
        {
            foreach (IFetchLicense fetcher in fetchers)
            {
                if (await TryFetchLicenseAsync(fetcher, package))
                {
                    break;
                }
            }
        }

        LogFetchResult(package);
    }

    private void LogFetchConfiguration()
    {
        if (gitHubApiKey is not null)
        {
            logger.LogInformation("Using GitHub API key");
        }
    }

    private async Task<bool> TryFetchLicenseAsync(IFetchLicense fetcher, PackageInfo package)
    {
        try
        {
            await fetcher.FetchLicenseAsync(package);
            return package.License is not null;
        }
        catch (HttpRequestException ex)
        {
            LogTransportFailure(fetcher, package, ex);
        }
        catch (JsonException ex)
        {
            LogJsonFailure(fetcher, package, ex);
        }

        return false;
    }

    private void LogTransportFailure(IFetchLicense fetcher, PackageInfo package, HttpRequestException ex)
    {
        logger.LogWarning(ex, "License fetcher {Fetcher} failed for {Name} {Version}: {ErrorMessage}",
            fetcher.GetType().Name, package.Name, package.Version, ex.Message);
    }

    private void LogJsonFailure(IFetchLicense fetcher, PackageInfo package, JsonException ex)
    {
        logger.LogWarning(ex, "License fetcher {Fetcher} returned invalid JSON for {Name} {Version}: {ErrorMessage}",
            fetcher.GetType().Name, package.Name, package.Version, ex.Message);
    }

    private void LogFetchResult(PackageInfo package)
    {
        if (package.License is null)
        {
            logger.LogWarning("Unable to determine license for package {Name} {Version}", package.Name, package.Version);
            package.License = "Unknown";
        }
        else
        {
            logger.LogInformation("License found for {Name}: {License}", package.Name, package.License);
        }
    }
}
