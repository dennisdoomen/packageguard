using PackageGuard.Core.Package;

namespace PackageGuard.Core.Risk;

/// <summary>
/// Implemented by risk enrichers whose upstream API can answer for many packages at once, so that the whole set can
/// be looked up before the per-package enrichment runs.
/// </summary>
internal interface IPrimeRiskData
{
    /// <summary>
    /// Looks up <paramref name="packages"/> in as few requests as the upstream API allows, caching the results for the
    /// per-package enrichment that follows. Failures are absorbed: enrichment then falls back to per-package requests.
    /// </summary>
    /// <param name="packages">Every package that is about to be enriched.</param>
    Task PrimeAsync(IReadOnlyCollection<PackageInfo> packages);
}
