using System.IO.Compression;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Pathy;

namespace PackageGuard.Core.GitHub;

/// <summary>
/// Caches the assembled risk profile of a repository, keyed by the repository itself rather than by the packages that
/// come out of it.
/// </summary>
/// <remarks>
/// Dozens of packages can share one repository, and its profile is the same for all of them. Keying the cache by
/// repository means a repository is described once per run, one package going stale cannot force the whole profile to
/// be collected again, and the profile survives to the next run.
/// </remarks>
internal sealed class GitHubRepositoryRiskCache(ILogger logger)
{
    /// <summary>
    /// The cached profiles, keyed by the repository's API root URL.
    /// </summary>
    private readonly Dictionary<string, GitHubRepositoryRiskCacheEntry> entries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Guards access to <see cref="entries"/>.
    /// </summary>
    private readonly Lock entriesLock = new();

    /// <summary>
    /// How long a cached profile is reused before it is collected again.
    /// </summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Ignores what is on disk and collects every profile again.
    /// </summary>
    public bool IsRefreshForced { get; set; }

    /// <summary>
    /// Returns the cached profile for <paramref name="repositoryApiRoot"/> when it is still fresh enough to reuse.
    /// A cached <see langword="null"/> records a repository that could not be read, so that the packages behind it do
    /// not each retry the same failing lookup.
    /// </summary>
    /// <param name="repositoryApiRoot">The API root URL of the repository.</param>
    /// <param name="data">The cached profile, which is <see langword="null"/> for a repository that could not be read.</param>
    /// <returns><see langword="true"/> when the cache can answer for this repository.</returns>
    public bool TryGet(string repositoryApiRoot, out GitHubRepositoryRiskData? data)
    {
        data = null;

        lock (entriesLock)
        {
            if (!entries.TryGetValue(repositoryApiRoot, out GitHubRepositoryRiskCacheEntry? entry) || IsStale(entry))
            {
                return false;
            }

            entry.IsUsed = true;
            data = entry.Data;
            return true;
        }
    }

    /// <summary>
    /// Stores the profile of a repository, or a <see langword="null"/> profile for one that could not be read.
    /// </summary>
    /// <param name="repositoryApiRoot">The API root URL of the repository.</param>
    /// <param name="data">The profile to cache, or <see langword="null"/> when it could not be collected.</param>
    public void Store(string repositoryApiRoot, GitHubRepositoryRiskData? data)
    {
        lock (entriesLock)
        {
            entries[repositoryApiRoot] = new GitHubRepositoryRiskCacheEntry
            {
                RepositoryApiRoot = repositoryApiRoot,
                Data = data,
                StoredAt = DateTimeOffset.UtcNow,
                IsUsed = true
            };
        }
    }

    /// <summary>
    /// Loads the profiles persisted next to the package cache at <paramref name="cacheFilePath"/>.
    /// </summary>
    /// <param name="cacheFilePath">The path of the package cache file this cache sits next to.</param>
    public async Task LoadAsync(string cacheFilePath)
    {
        string path = GetFilePath(cacheFilePath);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            GitHubRepositoryRiskCacheEntry[] loaded = await ReadEntriesAsync(path);
            Merge(loaded);
            logger.LogDebug("Loaded {Count} cached GitHub repository profiles from {Path}", loaded.Length, path);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not load the GitHub repository profile cache from {Path}", path);
        }
    }

    /// <summary>
    /// Persists the profiles that were used during this run, next to the package cache at
    /// <paramref name="cacheFilePath"/>.
    /// </summary>
    /// <param name="cacheFilePath">The path of the package cache file this cache sits next to.</param>
    public async Task SaveAsync(string cacheFilePath)
    {
        string path = GetFilePath(cacheFilePath);
        GitHubRepositoryRiskCacheEntry[] used = TakeEntriesWorthKeeping();

        try
        {
            path.ToPath().Directory.CreateDirectoryRecursively();
            await WriteEntriesAsync(path, used);
            logger.LogDebug("Persisted {Count} GitHub repository profiles to {Path}", used.Length, path);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not persist the GitHub repository profile cache to {Path}", path);
        }
    }

    /// <summary>
    /// Discards every cached profile. Only used by the tests.
    /// </summary>
    internal void Clear()
    {
        lock (entriesLock)
        {
            entries.Clear();
        }
    }

    /// <summary>
    /// Returns the entries worth keeping: the ones collected or reused during this run, and the ones that are still
    /// within their maximum age.
    /// </summary>
    /// <remarks>
    /// A run where every package was already up to date touches no profile at all. Keeping only what this run touched
    /// would empty the cache on such a run and undo the point of having it.
    /// </remarks>
    private GitHubRepositoryRiskCacheEntry[] TakeEntriesWorthKeeping()
    {
        lock (entriesLock)
        {
            return entries.Values.Where(entry => entry.IsUsed || IsWithinMaxAge(entry)).ToArray();
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when an entry has not yet reached its maximum age, regardless of whether a
    /// refresh was asked for.
    /// </summary>
    private bool IsWithinMaxAge(GitHubRepositoryRiskCacheEntry entry) =>
        DateTimeOffset.UtcNow - entry.StoredAt <= MaxAge;

    /// <summary>
    /// Adds the loaded entries, keeping any entry already collected during this run.
    /// </summary>
    private void Merge(GitHubRepositoryRiskCacheEntry[] loaded)
    {
        lock (entriesLock)
        {
            foreach (GitHubRepositoryRiskCacheEntry entry in loaded)
            {
                entries.TryAdd(entry.RepositoryApiRoot, entry);
            }
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when an entry is too old to reuse, or when a refresh was asked for.
    /// </summary>
    private bool IsStale(GitHubRepositoryRiskCacheEntry entry) => IsRefreshForced || !IsWithinMaxAge(entry);

    /// <summary>
    /// Reads and decompresses the persisted entries.
    /// </summary>
    private static async Task<GitHubRepositoryRiskCacheEntry[]> ReadEntriesAsync(string path)
    {
        await using FileStream fileStream = new(path, FileMode.Open, FileAccess.Read);
        await using BrotliStream decompressor = new(fileStream, CompressionMode.Decompress);
        return await MemoryPackSerializer.DeserializeAsync<GitHubRepositoryRiskCacheEntry[]>(decompressor) ?? [];
    }

    /// <summary>
    /// Compresses and writes the given entries.
    /// </summary>
    private static async Task WriteEntriesAsync(string path, GitHubRepositoryRiskCacheEntry[] entries)
    {
        await using FileStream fileStream = new(path, FileMode.Create, FileAccess.Write);
        await using BrotliStream compressor = new(fileStream, CompressionLevel.Fastest);
        await MemoryPackSerializer.SerializeAsync(compressor, entries);
    }

    /// <summary>
    /// Derives the cache file path from the path of the package cache file.
    /// </summary>
    private static string GetFilePath(string cacheFilePath) =>
        cacheFilePath.ToPath().Directory / "github-repositories.bin";
}
