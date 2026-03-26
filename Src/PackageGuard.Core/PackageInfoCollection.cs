using System.Collections;
using MemoryPack;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Core.Types;
using Pathy;

namespace PackageGuard.Core;

/// <summary>
/// Tracks package metadata discovered during analysis and reuses cached package state when possible.
/// </summary>
public class PackageInfoCollection(ILogger logger, AnalyzerSettings? settings = null) : IEnumerable<PackageInfo>
{
    /// <summary>
    /// Keyed collection of packages loaded or added during the current analysis run.
    /// </summary>
    private readonly Dictionary<string, PackageInfo> packages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Persisted cache of package metadata keyed by collection key, used to avoid redundant fetches across runs.
    /// </summary>
    private Dictionary<string, PackageInfo> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Guards against loading the cache file more than once per <see cref="PackageInfoCollection"/> instance.
    /// </summary>
    private bool isCacheInitialized;

    /// <summary>
    /// Returns an enumerator that iterates over all packages in the collection.
    /// </summary>
    public IEnumerator<PackageInfo> GetEnumerator() => packages.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => packages.Values.GetEnumerator();

    /// <summary>
    /// Adds the package to the collection and returns the canonical instance for its source, name, and version.
    /// </summary>
    public PackageInfo Add(PackageInfo package)
    {
        string packageKey = package.GetCollectionKey();
        PackageInfo existingPackage = GetOrAddCachedPackage(packageKey, package);

        packages[packageKey] = existingPackage;
        existingPackage.MarkAsUsed();

        UpdateLicenseForWellKnownLicenseUrls(existingPackage);
        return existingPackage;
    }

    /// <summary>
    /// If the package has no license but shares a license URL with a cached package that does, copies that license across.
    /// </summary>
    private void UpdateLicenseForWellKnownLicenseUrls(PackageInfo package)
    {
        if (package.License is null && package.LicenseUrl is not null)
        {
            package.License = cache.Values.FirstOrDefault(x => x.LicenseUrl == package.LicenseUrl)?.License;
        }
    }

    /// <summary>
    /// Finds a package for the requested sources, reusing cache entries when they are still valid.
    /// </summary>
    public PackageInfo? Find(string name, string version, SourceRepository[] projectNuGetSources)
    {
        string[] sourceUrls = projectNuGetSources
            .Select(source => source.PackageSource.Source)
            .ToArray();

        PackageInfo? package = FindLoadedPackage(name, version, sourceUrls) ?? FindCachedPackage(name, version, sourceUrls);

        if (package is not null)
        {
            package.MarkAsUsed();
            return package;
        }

        return null;
    }

    /// <summary>
    /// Initializes the cache from the persisted package metadata file if it exists.
    /// </summary>
    public async Task TryInitializeFromCache(string cacheFilePath)
    {
        if (isCacheInitialized)
        {
            return;
        }

        await TryLoadCache(cacheFilePath);
        isCacheInitialized = true;
    }

    /// <summary>
    /// Reads and deserialises the cache file at <paramref name="cacheFilePath"/> into the in-memory cache,
    /// logging a warning if the file cannot be read or parsed.
    /// </summary>
    private async Task TryLoadCache(string cacheFilePath)
    {
        if (!File.Exists(cacheFilePath))
        {
            return;
        }

        try
        {
            await using FileStream fileStream = new(cacheFilePath, FileMode.Open, FileAccess.Read);
            PackageInfo[]? cachedPackages = await MemoryPackSerializer.DeserializeAsync<PackageInfo[]>(fileStream);
            if (cachedPackages is not null) cache = BuildCache(cachedPackages);

            logger.LogInformation("Successfully loaded the cache from {CacheFilePath}", cacheFilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not load package cache from {CacheFilePath}: {ErrorMessage}", cacheFilePath, ex.Message);
        }
    }

    /// <summary>
    /// Writes the currently used package metadata back to the configured cache file.
    /// </summary>
    public async Task WriteToCache(string cacheFilePath)
    {
        cacheFilePath.ToPath().Directory.CreateDirectoryRecursively();

        await using FileStream fileStream = new(cacheFilePath, FileMode.Create, FileAccess.Write);
        try
        {
            PackageInfo[] usedPackages = cache.Values.Where(c => c.IsUsed).ToArray();
            StampCacheEntries(usedPackages);
            await MemoryPackSerializer.SerializeAsync(fileStream, usedPackages);
            logger.LogInformation("Package cache written to {CacheFilePath}", cacheFilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to write package cache {CacheFilePath}: {ErrorMessage}", cacheFilePath, ex.Message);
        }
    }

    /// <summary>
    /// Sets <see cref="PackageInfo.CacheUpdatedAt"/> to the current UTC time on every entry before serialisation.
    /// </summary>
    private static void StampCacheEntries(IEnumerable<PackageInfo> packages)
    {
        DateTimeOffset cacheUpdatedAt = DateTimeOffset.UtcNow;
        foreach (PackageInfo package in packages)
        {
            if (package.CacheUpdatedAt == default)
            {
                package.CacheUpdatedAt = cacheUpdatedAt;
            }
        }
    }

    /// <summary>
    /// Returns all packages that were used during the current analysis run.
    /// </summary>
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

    /// <summary>
    /// Returns the existing cached entry for <paramref name="packageKey"/> after merging state from
    /// <paramref name="package"/> into it, or adds <paramref name="package"/> to the cache and returns it directly.
    /// </summary>
    private PackageInfo GetOrAddCachedPackage(string packageKey, PackageInfo package)
    {
        if (!cache.TryGetValue(packageKey, out PackageInfo? existingPackage))
        {
            cache[packageKey] = package;
            return package;
        }

        MergePackageState(existingPackage, package);
        return existingPackage;
    }

    /// <summary>
    /// Searches the in-memory loaded packages for an entry matching the given name, version, and source URLs.
    /// </summary>
    private PackageInfo? FindLoadedPackage(string name, string version, string[] sourceUrls)
    {
        return packages.Values.FirstOrDefault(p => MatchesPackage(p, name, version, sourceUrls));
    }

    /// <summary>
    /// Searches the cache for a reusable entry matching the given name, version, and source URLs,
    /// promoting it to the active package map if found.
    /// </summary>
    private PackageInfo? FindCachedPackage(string name, string version, string[] sourceUrls)
    {
        PackageInfo? package = cache.Values.FirstOrDefault(p => MatchesPackage(p, name, version, sourceUrls) && ShouldReuseCachedPackage(p));
        if (package is not null)
        {
            packages[package.GetCollectionKey()] = package;
        }

        return package;
    }

    /// <summary>
    /// Returns <c>true</c> when the given package matches the requested name, version, and one of the provided source URLs.
    /// </summary>
    private static bool MatchesPackage(PackageInfo package, string name, string version, string[] sourceUrls)
    {
        return package.Name == name &&
            package.Version == version &&
            sourceUrls.Contains(package.SourceUrl, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deserializes <paramref name="cachedPackages"/> into a lookup dictionary, merging duplicate keys.
    /// </summary>
    private static Dictionary<string, PackageInfo> BuildCache(IEnumerable<PackageInfo> cachedPackages)
    {
        Dictionary<string, PackageInfo> cachedEntries = new(StringComparer.OrdinalIgnoreCase);
        foreach (PackageInfo cachedPackage in cachedPackages)
        {
            string packageKey = cachedPackage.GetCollectionKey();
            if (!cachedEntries.TryGetValue(packageKey, out PackageInfo? existingPackage))
            {
                cachedEntries[packageKey] = cachedPackage;
                continue;
            }

            MergePackageState(existingPackage, cachedPackage);
        }

        return cachedEntries;
    }

    /// <summary>
    /// Merges metadata, project references, and usage state from <paramref name="source"/> into <paramref name="target"/>.
    /// </summary>
    private static void MergePackageState(PackageInfo target, PackageInfo source)
    {
        MergePackageMetadata(target, source);
        MergeProjects(target, source);
        if (source.IsUsed) target.MarkAsUsed();
    }

    /// <summary>
    /// Copies missing metadata fields (source, source URL, repository URL, license, license URL)
    /// from <paramref name="source"/> into <paramref name="target"/> without overwriting existing values.
    /// </summary>
    private static void MergePackageMetadata(PackageInfo target, PackageInfo source)
    {
        if (string.IsNullOrWhiteSpace(target.Source) && !string.IsNullOrWhiteSpace(source.Source)) target.Source = source.Source;
        if (string.IsNullOrWhiteSpace(target.SourceUrl) && !string.IsNullOrWhiteSpace(source.SourceUrl)) target.SourceUrl = source.SourceUrl;

        target.RepositoryUrl ??= source.RepositoryUrl;
        target.License ??= source.License;
        target.LicenseUrl ??= source.LicenseUrl;
    }

    /// <summary>
    /// Adds any project paths from <paramref name="source"/> that are not already tracked by <paramref name="target"/>.
    /// </summary>
    private static void MergeProjects(PackageInfo target, PackageInfo source)
    {
        foreach (string projectPath in source.Projects.Except(target.Projects, StringComparer.OrdinalIgnoreCase))
        {
            target.TrackAsUsedInProject(projectPath);
        }
    }

    /// <summary>
    /// Builds a dictionary keyed by each used package's dependency key for fast lookup during graph traversal.
    /// </summary>
    internal IReadOnlyDictionary<string, PackageInfo> CreatePackagesByKey() =>
        GetAllUsedPackages()
            .GroupBy(p => p.CreatePackageKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <c>true</c> if the cached package entry is fresh enough to be reused without refetching risk data.
    /// </summary>
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
