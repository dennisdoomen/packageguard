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
    /// <summary>
    /// Ordered list of license-fetching strategies tried in sequence until one succeeds.
    /// </summary>
    private readonly IReadOnlyList<IFetchLicense> fetchers =
        [
            new CorrectMisbehavingPackagesFetcher(),
            new GitHubLicenseFetcher(gitHubApiKey),
            new UrlLicenseFetcher(logger)
        ];

    /// <summary>
    /// Test-only constructor that accepts an explicit set of license fetcher strategies.
    /// </summary>
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

    /// <summary>
    /// Logs whether a GitHub API key is configured before attempting to fetch the license.
    /// </summary>
    private void LogFetchConfiguration()
    {
        if (gitHubApiKey is not null)
        {
            logger.LogInformation("Using GitHub API key");
        }
    }

    /// <summary>
    /// Invokes a single <see cref="IFetchLicense"/> strategy and returns <c>true</c> if it populated
    /// <see cref="PackageInfo.License"/> successfully.
    /// </summary>
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

    /// <summary>
    /// Logs a warning when an <see cref="IFetchLicense"/> strategy fails due to an HTTP transport error.
    /// </summary>
    private void LogTransportFailure(IFetchLicense fetcher, PackageInfo package, HttpRequestException ex)
    {
        logger.LogWarning(ex, "License fetcher {Fetcher} failed for {Name} {Version}: {ErrorMessage}",
            fetcher.GetType().Name, package.Name, package.Version, ex.Message);
    }

    /// <summary>
    /// Logs a warning when an <see cref="IFetchLicense"/> strategy returns malformed JSON.
    /// </summary>
    private void LogJsonFailure(IFetchLicense fetcher, PackageInfo package, JsonException ex)
    {
        logger.LogWarning(ex, "License fetcher {Fetcher} returned invalid JSON for {Name} {Version}: {ErrorMessage}",
            fetcher.GetType().Name, package.Name, package.Version, ex.Message);
    }

    /// <summary>
    /// Logs the outcome of the license fetch attempt: a warning if no license was found (and sets it to "Unknown"),
    /// or an info message with the resolved license identifier.
    /// </summary>
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
