namespace PackageGuard.Core;

internal interface IEnrichPackageRisk
{
    Task EnrichAsync(PackageInfo package);
}
