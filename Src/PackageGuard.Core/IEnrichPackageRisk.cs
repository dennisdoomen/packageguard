namespace PackageGuard.Core;

internal interface IEnrichPackageRisk
{
    bool HasCachedData(PackageInfo package);

    Task EnrichAsync(PackageInfo package);
}
