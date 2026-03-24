using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

internal sealed class PackageRiskEnricher
{
    private readonly IEnrichPackageRisk[] enrichers;

    public PackageRiskEnricher(ILogger logger, string? gitHubApiKey)
    {
        enrichers =
        [
            new LicenseUrlRiskEnricher(logger),
            new OsvRiskEnricher(),
            new GitHubRepositoryRiskEnricher(logger, gitHubApiKey)
        ];
    }

    public async Task EnrichAsync(IEnumerable<PackageInfo> packages)
    {
        foreach (PackageInfo package in packages)
        {
            foreach (IEnrichPackageRisk enricher in enrichers)
            {
                await enricher.EnrichAsync(package);
            }
        }
    }
}
