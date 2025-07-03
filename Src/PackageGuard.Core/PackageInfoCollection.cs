using System.Collections;
using MemoryPack;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Core.Types;
using Pathy;

namespace PackageGuard.Core;

public class PackageInfoCollection(ILogger logger) : IEnumerable<PackageInfo>
{
    private HashSet<PackageInfo> packages = new();

    public IEnumerator<PackageInfo> GetEnumerator() => packages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)packages).GetEnumerator();

    public void Add(PackageInfo package)
    {
        packages.Add(package);
        UpdateLicenseForWellKnownLicenseUrls(package);
    }

    private void UpdateLicenseForWellKnownLicenseUrls(PackageInfo package)
    {
        if (package.License is null && package.LicenseUrl is not null)
        {
            package.License = packages.FirstOrDefault(x => x.LicenseUrl == package.LicenseUrl)?.License;
        }
    }

    public PackageInfo? Find(string name, string version, SourceRepository[] projectNuGetSources)
    {
        string[] sourceUrls = projectNuGetSources
            .Select(source => source.PackageSource.Source)
            .ToArray();

        PackageInfo? package = packages.FirstOrDefault(p => p.Name == name && p.Version == version);
        if (package is not null)
        {
            if (sourceUrls.Contains(package.SourceUrl))
            {
                return package;
            }

            logger.LogWarning("Found package {Name} {Version}, but its source {SourceUrl} is not available for this project",
                name, version, package.SourceUrl);
        }

        return null;
    }

    public async Task TryInitializeFromCache(string cacheFilePath)
    {
        if (File.Exists(cacheFilePath))
        {
            try
            {
                await using FileStream fileStream = new(cacheFilePath, FileMode.Open, FileAccess.Read);
                PackageInfo[]? cachedPackages = await MemoryPackSerializer.DeserializeAsync<PackageInfo[]>(fileStream);

                if (cachedPackages is not null)
                {
                    packages = new HashSet<PackageInfo>(cachedPackages);
                }

                logger.LogInformation("Successfully loaded the cache from {CacheFilePath}", cacheFilePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not load package cache from {CacheFilePath}: {ErrorMessage}", cacheFilePath, ex.Message);
            }
        }
    }

    public async Task WriteToCache(string cacheFilePath)
    {
        cacheFilePath.ToPath().Directory.CreateDirectoryRecursively();

        await using FileStream fileStream = new(cacheFilePath, FileMode.Create, FileAccess.Write);
        try
        {
            await MemoryPackSerializer.SerializeAsync(fileStream, packages.ToArray());
            logger.LogInformation("Package cache written to {CacheFilePath}", cacheFilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to write package cache {CacheFilePath}: {ErrorMessage}", cacheFilePath, ex.Message);
        }
    }
}
