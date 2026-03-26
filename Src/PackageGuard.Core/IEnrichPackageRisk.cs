namespace PackageGuard.Core;

/// <summary>
/// Enriches a <see cref="PackageInfo"/> with additional risk-related metadata from an external source.
/// </summary>
internal interface IEnrichPackageRisk
{
    /// <summary>
    /// Returns <c>true</c> when the enricher already has cached data available for the given package,
    /// allowing the caller to skip a remote fetch.
    /// </summary>
    bool HasCachedData(PackageInfo package);

    /// <summary>
    /// Fetches and populates risk metadata for the given package from the enricher's data source.
    /// </summary>
    Task EnrichAsync(PackageInfo package);
}
