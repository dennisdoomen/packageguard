using Microsoft.Extensions.Logging;
using PackageGuard.Core.FetchingStrategies;

namespace PackageGuard.Core;

/// <summary>
/// Responsible for fetching and amending missing license information for a package.
/// </summary>
public sealed class LicenseFetcher(ILogger logger, string? gitHubApiKey = null)
{
    private readonly IEnumerable<IFetchLicense> fetchers =
    [
        new GitHubLicenseFetcher(gitHubApiKey),
        new UrlLicenseFetcher(logger)
    ];

    public async Task AmendWithMissingLicenseInformation(PackageInfo package)
    {
        if (package.License is not null)
        {
            logger.LogInformation("Package {Name} already has a license: {License}", package.Id, package.License);
            return;
        }

        foreach (IFetchLicense fetcher in fetchers)
        {
            await fetcher.FetchLicenseAsync(package);

            if (package.License is not null)
            {
                logger.LogInformation("License found for {Name}: {License}", package.Id, package.License);
                break;
            }
        }

        if (package.License is null)
        {
            logger.LogWarning("Unable to determine license for package {Name} {Version}", package.Id, package.Version);
        }
    }
}
