using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

internal sealed class PackageRiskEnricher
{
    private const int MaxConcurrentPackages = 6;
    private readonly IEnrichPackageRisk[] enrichers;

    public PackageRiskEnricher(ILogger logger, string? gitHubApiKey)
        : this(
            [
                new LicenseUrlRiskEnricher(logger),
                new NuGetPackageSigningRiskEnricher(logger),
                new OsvRiskEnricher(),
                new GitHubRepositoryRiskEnricher(logger, gitHubApiKey)
            ])
    {
    }

    internal PackageRiskEnricher(params IEnrichPackageRisk[] enrichers)
    {
        this.enrichers = enrichers;
    }

    public async Task EnrichAsync(IEnumerable<PackageInfo> packages)
    {
        PackageInfo[] packageArray = packages as PackageInfo[] ?? packages.ToArray();
        if (packageArray.Length == 0)
        {
            return;
        }

        await Parallel.ForEachAsync(packageArray,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(MaxConcurrentPackages, packageArray.Length)
            },
            async (package, _) =>
            {
                foreach (IEnrichPackageRisk enricher in enrichers)
                {
                    if (enricher.HasCachedData(package))
                    {
                        continue;
                    }

                    await enricher.EnrichAsync(package);
                }
            });
    }
}
