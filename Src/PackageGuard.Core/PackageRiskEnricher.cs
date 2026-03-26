using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

/// <summary>
/// Enriches a collection of <see cref="PackageInfo"/> instances with risk data by running all registered enrichers in parallel.
/// </summary>
internal sealed class PackageRiskEnricher
{
    /// <summary>
    /// The maximum number of packages processed concurrently during enrichment.
    /// </summary>
    private const int MaxConcurrentPackages = 6;

    /// <summary>
    /// The set of risk enrichers applied to each package.
    /// </summary>
    private readonly IEnrichPackageRisk[] enrichers;

    /// <summary>
    /// Initializes a new instance of <see cref="PackageRiskEnricher"/> with the default set of enrichers.
    /// </summary>
    /// <param name="logger">The logger used by the individual enrichers.</param>
    /// <param name="gitHubApiKey">An optional GitHub API key used by the GitHub repository enricher.</param>
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

    /// <summary>
    /// Initializes a new instance of <see cref="PackageRiskEnricher"/> with an explicit list of enrichers.
    /// </summary>
    /// <param name="enrichers">The enrichers to apply during enrichment.</param>
    internal PackageRiskEnricher(params IEnrichPackageRisk[] enrichers)
    {
        this.enrichers = enrichers;
    }

    /// <summary>
    /// Enriches each package in <paramref name="packages"/> with risk information from all registered enrichers.
    /// </summary>
    /// <param name="packages">The packages to enrich.</param>
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
