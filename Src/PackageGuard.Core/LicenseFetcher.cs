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

    public async Task AmendWithMissingLicenseInformation(PackageInfo package)
    {
        if (gitHubApiKey is not null)
        {
            logger.LogInformation("Using GitHub API key");
        }

        if (package.License is null)
        {
            foreach (IFetchLicense fetcher in fetchers)
            {
                try
                {
                    await fetcher.FetchLicenseAsync(package);
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "License fetcher {Fetcher} failed for {Name} {Version}: {ErrorMessage}",
                        fetcher.GetType().Name, package.Name, package.Version, ex.Message);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "License fetcher {Fetcher} returned invalid JSON for {Name} {Version}: {ErrorMessage}",
                        fetcher.GetType().Name, package.Name, package.Version, ex.Message);
                }

                if (package.License is not null)
                {
                    break;
                }
            }
        }

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
