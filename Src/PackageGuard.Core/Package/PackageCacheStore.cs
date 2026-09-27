using Microsoft.Extensions.Logging;
using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Package;

/// <summary>
/// Owns the lifecycle of the on-disk package and GitHub API caches for a single analysis run.
/// Construct a fresh instance per run - <see cref="LoadAsync"/> must run before <see cref="PersistAsync"/>.
/// </summary>
internal sealed class PackageCacheStore(ILogger logger)
{
    private bool isCachingEnabled;

    public async Task LoadAsync(AnalyzerSettings settings, PackageInfoCollection packages)
    {
        isCachingEnabled = settings is { UseCaching: true, CacheFilePath.Length: > 0 };
        if (isCachingEnabled)
        {
            logger.LogInformation("Try loading package cache from {CacheFilePath}", settings.CacheFilePath);
            await packages.TryInitializeFromCache(settings.CacheFilePath);
            await GitHubApi.LoadCachesAsync(logger, settings.CacheFilePath, settings);
        }
    }

    public async Task PersistAsync(AnalyzerSettings settings, PackageInfoCollection packages)
    {
        if (settings.UseCaching)
        {
            await packages.WriteToCache(settings.CacheFilePath);
        }

        if (isCachingEnabled)
        {
            await GitHubApi.SaveCachesAsync(logger, settings.CacheFilePath);
        }
    }
}
