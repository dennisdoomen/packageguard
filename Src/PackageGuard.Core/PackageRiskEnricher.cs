using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

internal sealed class PackageRiskEnricher(ILogger logger, string? gitHubApiKey)
{
    private const int MaxConcurrentPackages = 6;
    private readonly IEnrichPackageRisk[] enrichers =
    [
        new LicenseUrlRiskEnricher(logger),
        new NuGetPackageSigningRiskEnricher(logger),
        new OsvRiskEnricher(),
        new GitHubRepositoryRiskEnricher(logger, gitHubApiKey)
    ];

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
                    await enricher.EnrichAsync(package);
                }
            });
    }
}
