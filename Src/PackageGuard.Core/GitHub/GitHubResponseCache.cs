using System.IO.Compression;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Pathy;

namespace PackageGuard.Core.GitHub;

/// <summary>
/// Stores GitHub API responses together with their entity tags so that later runs can revalidate them with a
/// conditional request. GitHub does not charge a <c>304 Not Modified</c> against the primary rate limit of an
/// authenticated caller, which makes repeated runs far cheaper.
/// </summary>
internal sealed class GitHubResponseCache(ILogger logger)
{
    /// <summary>
    /// Cached responses keyed by request URL.
    /// </summary>
    private readonly Dictionary<string, GitHubResponseCacheEntry> entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Guards concurrent access to <see cref="entries"/>.
    /// </summary>
    private readonly Lock entriesLock = new();

    /// <summary>
    /// Responses larger than this are not persisted, keeping the cache file to a sensible size.
    /// </summary>
    private const int MaxCacheableBodyLength = 512 * 1024;

    /// <summary>
    /// How long a "resource does not exist" answer is trusted before it is probed again.
    /// </summary>
    private static readonly TimeSpan NotFoundLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Returns the cached entry for <paramref name="url"/>, or <see langword="null"/> when nothing is cached.
    /// </summary>
    public GitHubResponseCacheEntry? Find(string url)
    {
        lock (entriesLock)
        {
            if (!entries.TryGetValue(url, out GitHubResponseCacheEntry? entry))
            {
                return null;
            }

            entry.IsUsed = true;
            return entry;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when a recent request established that <paramref name="url"/> does not exist.
    /// </summary>
    public bool IsKnownToBeMissing(string url)
    {
        GitHubResponseCacheEntry? entry = Find(url);
        return entry is { IsNotFound: true } && DateTimeOffset.UtcNow - entry.StoredAt <= NotFoundLifetime;
    }

    /// <summary>
    /// Returns the body of a response the API already confirmed during this run, or <see langword="null"/> when the
    /// URL has not been requested yet. Several components ask for the same repository, and GitHub data does not
    /// change over the course of a single run.
    /// </summary>
    public string? FindFreshBody(string url)
    {
        GitHubResponseCacheEntry? entry = Find(url);
        return entry is { IsFreshThisRun: true, IsNotFound: false } ? entry.Body : null;
    }

    /// <summary>
    /// Stores a successful response body and its entity tag.
    /// </summary>
    public void StoreResponse(string url, string? eTag, string body)
    {
        if (body.Length > MaxCacheableBodyLength)
        {
            Remove(url);
            return;
        }

        Store(url, entry =>
        {
            entry.ETag = eTag;
            entry.Body = body;
            entry.IsNotFound = false;
        });
    }

    /// <summary>
    /// Records that <paramref name="url"/> does not exist, so the same probe is not repeated on every run.
    /// </summary>
    public void StoreNotFound(string url) => Store(url, entry =>
    {
        entry.ETag = null;
        entry.Body = string.Empty;
        entry.IsNotFound = true;
    });

    /// <summary>
    /// Refreshes the revalidation timestamp of an entry that the API confirmed as unchanged.
    /// </summary>
    public void MarkAsRevalidated(string url) => Store(url, _ => { });

    /// <summary>
    /// Loads previously persisted responses from <paramref name="cacheFilePath"/>, ignoring a missing or unreadable file.
    /// </summary>
    public async Task LoadAsync(string cacheFilePath)
    {
        if (!File.Exists(cacheFilePath))
        {
            return;
        }

        try
        {
            GitHubResponseCacheEntry[] loaded = await ReadEntriesAsync(cacheFilePath);
            lock (entriesLock)
            {
                foreach (GitHubResponseCacheEntry entry in loaded)
                {
                    entries[entry.Url] = entry;
                }
            }

            logger.LogDebug("Loaded {Count} cached GitHub responses from {CacheFilePath}", loaded.Length, cacheFilePath);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not load the GitHub response cache from {CacheFilePath}", cacheFilePath);
        }
    }

    /// <summary>
    /// Persists every entry that was used during this run to <paramref name="cacheFilePath"/>.
    /// </summary>
    public async Task SaveAsync(string cacheFilePath)
    {
        GitHubResponseCacheEntry[] used;
        lock (entriesLock)
        {
            used = entries.Values.Where(entry => entry.IsUsed).ToArray();
        }

        try
        {
            cacheFilePath.ToPath().Directory.CreateDirectoryRecursively();
            await WriteEntriesAsync(cacheFilePath, used);
            logger.LogDebug("Persisted {Count} cached GitHub responses to {CacheFilePath}", used.Length, cacheFilePath);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not persist the GitHub response cache to {CacheFilePath}", cacheFilePath);
        }
    }

    /// <summary>
    /// Reads and decompresses the persisted entries.
    /// </summary>
    private static async Task<GitHubResponseCacheEntry[]> ReadEntriesAsync(string cacheFilePath)
    {
        await using FileStream fileStream = new(cacheFilePath, FileMode.Open, FileAccess.Read);
        await using BrotliStream decompressor = new(fileStream, CompressionMode.Decompress);
        return await MemoryPackSerializer.DeserializeAsync<GitHubResponseCacheEntry[]>(decompressor) ?? [];
    }

    /// <summary>
    /// Compresses and writes the given entries.
    /// </summary>
    private static async Task WriteEntriesAsync(string cacheFilePath, GitHubResponseCacheEntry[] used)
    {
        await using FileStream fileStream = new(cacheFilePath, FileMode.Create, FileAccess.Write);
        await using BrotliStream compressor = new(fileStream, CompressionLevel.Fastest);
        await MemoryPackSerializer.SerializeAsync(compressor, used);
    }

    /// <summary>
    /// Applies <paramref name="update"/> to the entry for <paramref name="url"/>, creating it when needed, and stamps
    /// it as freshly revalidated.
    /// </summary>
    private void Store(string url, Action<GitHubResponseCacheEntry> update)
    {
        lock (entriesLock)
        {
            if (!entries.TryGetValue(url, out GitHubResponseCacheEntry? entry))
            {
                entry = new GitHubResponseCacheEntry { Url = url };
                entries[url] = entry;
            }

            update(entry);
            entry.StoredAt = DateTimeOffset.UtcNow;
            entry.IsUsed = true;
            entry.IsFreshThisRun = true;
        }
    }

    /// <summary>
    /// Drops the entry for <paramref name="url"/>, if any.
    /// </summary>
    private void Remove(string url)
    {
        lock (entriesLock)
        {
            entries.Remove(url);
        }
    }
}
