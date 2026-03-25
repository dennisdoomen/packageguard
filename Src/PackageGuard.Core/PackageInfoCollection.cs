using System.Collections;
using MemoryPack;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Core.Types;
using Pathy;

namespace PackageGuard.Core;

public class PackageInfoCollection(ILogger logger, AnalyzerSettings? settings = null) : IEnumerable<PackageInfo>
{
    private readonly Dictionary<string, PackageInfo> packages = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PackageInfo> cache = new(StringComparer.OrdinalIgnoreCase);
    private bool isCacheInitialized;

    public IEnumerator<PackageInfo> GetEnumerator() => packages.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => packages.Values.GetEnumerator();

    public PackageInfo Add(PackageInfo package)
    {
        string packageKey = package.GetCollectionKey();
        if (!cache.TryGetValue(packageKey, out PackageInfo? existingPackage))
        {
            existingPackage = package;
            cache[packageKey] = existingPackage;
        }
        else if (!ReferenceEquals(existingPackage, package))
        {
            MergePackageState(existingPackage, package);
        }

        packages[packageKey] = existingPackage;
        existingPackage.MarkAsUsed();

        UpdateLicenseForWellKnownLicenseUrls(existingPackage);
        return existingPackage;
    }

    private void UpdateLicenseForWellKnownLicenseUrls(PackageInfo package)
    {
        if (package.License is null && package.LicenseUrl is not null)
        {
            package.License = cache.Values.FirstOrDefault(x => x.LicenseUrl == package.LicenseUrl)?.License;
        }
    }

    public PackageInfo? Find(string name, string version, SourceRepository[] projectNuGetSources)
    {
        string[] sourceUrls = projectNuGetSources
            .Select(source => source.PackageSource.Source)
            .ToArray();

        PackageInfo? package = packages.Values.FirstOrDefault(p =>
            p.Name == name &&
            p.Version == version &&
            sourceUrls.Contains(p.SourceUrl, StringComparer.OrdinalIgnoreCase));
        if (package is null)
        {
            package = cache.Values.FirstOrDefault(p =>
                p.Name == name &&
                p.Version == version &&
                sourceUrls.Contains(p.SourceUrl, StringComparer.OrdinalIgnoreCase) &&
                ShouldReuseCachedPackage(p));
            if (package is not null)
            {
                packages[package.GetCollectionKey()] = package;
            }
        }

        if (package is not null)
        {
            package.MarkAsUsed();
            return package;
        }

        return null;
    }

    public async Task TryInitializeFromCache(string cacheFilePath)
    {
        if (isCacheInitialized)
        {
            return;
        }

        if (File.Exists(cacheFilePath))
        {
            try
            {
                await using FileStream fileStream = new(cacheFilePath, FileMode.Open, FileAccess.Read);
                PackageInfo[]? cachedPackages = await MemoryPackSerializer.DeserializeAsync<PackageInfo[]>(fileStream);

                if (cachedPackages is not null)
                {
                    cache = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (PackageInfo cachedPackage in cachedPackages)
                    {
                        string packageKey = cachedPackage.GetCollectionKey();
                        if (!cache.TryGetValue(packageKey, out PackageInfo? existingPackage))
                        {
                            cache[packageKey] = cachedPackage;
                        }
                        else
                        {
                            MergePackageState(existingPackage, cachedPackage);
                        }
                    }
                }

                logger.LogInformation("Successfully loaded the cache from {CacheFilePath}", cacheFilePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not load package cache from {CacheFilePath}: {ErrorMessage}", cacheFilePath, ex.Message);
            }
        }

        isCacheInitialized = true;
    }

    public async Task WriteToCache(string cacheFilePath)
    {
        cacheFilePath.ToPath().Directory.CreateDirectoryRecursively();

        await using FileStream fileStream = new(cacheFilePath, FileMode.Create, FileAccess.Write);
        try
        {
            PackageInfo[] usedPackages = cache.Values.Where(c => c.IsUsed).ToArray();
            DateTimeOffset cacheUpdatedAt = DateTimeOffset.UtcNow;
            foreach (PackageInfo package in usedPackages)
            {
                package.CacheUpdatedAt = cacheUpdatedAt;
            }

            await MemoryPackSerializer.SerializeAsync(fileStream, usedPackages);
            logger.LogInformation("Package cache written to {CacheFilePath}", cacheFilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to write package cache {CacheFilePath}: {ErrorMessage}", cacheFilePath, ex.Message);
        }
    }

    public PackageInfo[] GetAllUsedPackages()
    {
        return cache.Values.Where(package => package.IsUsed).ToArray();
    }

    /// <summary>
    /// Clears the list of packages collected.
    /// </summary>
    /// <remarks>
    /// Does not affect the cache.
    /// </remarks>
    public void Clear()
    {
        packages.Clear();
    }

    private static void MergePackageState(PackageInfo target, PackageInfo source)
    {
        if (string.IsNullOrWhiteSpace(target.Source) && !string.IsNullOrWhiteSpace(source.Source))
        {
            target.Source = source.Source;
        }

        if (string.IsNullOrWhiteSpace(target.SourceUrl) && !string.IsNullOrWhiteSpace(source.SourceUrl))
        {
            target.SourceUrl = source.SourceUrl;
        }

        target.RepositoryUrl ??= source.RepositoryUrl;
        target.License ??= source.License;
        target.LicenseUrl ??= source.LicenseUrl;

        foreach (string projectPath in source.Projects.Except(target.Projects, StringComparer.OrdinalIgnoreCase))
        {
            target.TrackAsUsedInProject(projectPath);
        }

        if (source.IsUsed)
        {
            target.MarkAsUsed();
        }
    }

    private bool ShouldReuseCachedPackage(PackageInfo package)
    {
        if (settings?.ReportRisk != true)
        {
            return true;
        }

        if (settings.RefreshRiskCache)
        {
            return false;
        }

        if (package.CacheUpdatedAt is null)
        {
            return false;
        }

        return DateTimeOffset.UtcNow - package.CacheUpdatedAt.Value <= settings.RiskCacheMaxAge;
    }
}
