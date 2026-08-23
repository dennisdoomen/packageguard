using Microsoft.Extensions.Logging;
using Pathy;

namespace PackageGuard.Core.GitHub;

/// <summary>
/// Hands out the <see cref="GitHubApiClient"/> shared by every component that talks to GitHub during a run.
/// </summary>
/// <remarks>
/// Licenses are fetched while projects are being scanned and risk signals are collected afterwards, so the two
/// phases only stay within a single rate limit budget if they use the same client. Clients are cached per token
/// because that is what identifies a budget.
/// </remarks>
internal static class GitHubApi
{
    /// <summary>
    /// The clients handed out so far, keyed by the token they authenticate with.
    /// </summary>
    private static readonly Dictionary<string, GitHubApiClient> ClientsByApiKey = new(StringComparer.Ordinal);

    /// <summary>
    /// The conditional-request cache shared by all clients.
    /// </summary>
    private static GitHubResponseCache? responseCache;

    /// <summary>
    /// The repository profile cache shared by every risk enricher.
    /// </summary>
    private static GitHubRepositoryRiskCache? repositoryCache;

    /// <summary>
    /// Guards the shared client and cache instances.
    /// </summary>
    private static readonly Lock SharedLock = new();

    /// <summary>
    /// Returns the client for <paramref name="apiKey"/>, creating it on first use.
    /// </summary>
    /// <param name="logger">The logger the client reports throttling to.</param>
    /// <param name="apiKey">The GitHub personal access token to authenticate with, if any.</param>
    public static GitHubApiClient GetOrCreateClient(ILogger logger, string? apiKey)
    {
        lock (SharedLock)
        {
            string cacheKey = apiKey ?? string.Empty;
            if (!ClientsByApiKey.TryGetValue(cacheKey, out GitHubApiClient? client))
            {
                responseCache ??= new GitHubResponseCache(logger);
                client = new GitHubApiClient(logger, apiKey, responseCache);
                ClientsByApiKey[cacheKey] = client;
            }

            return client;
        }
    }

    /// <summary>
    /// Returns the repository profile cache, creating it on first use.
    /// </summary>
    /// <param name="logger">The logger to report cache problems to.</param>
    public static GitHubRepositoryRiskCache GetOrCreateRepositoryCache(ILogger logger)
    {
        lock (SharedLock)
        {
            return repositoryCache ??= new GitHubRepositoryRiskCache(logger);
        }
    }

    /// <summary>
    /// Loads the responses and repository profiles cached next to the package cache at
    /// <paramref name="cacheFilePath"/>.
    /// </summary>
    /// <param name="logger">The logger to report cache problems to.</param>
    /// <param name="cacheFilePath">The path of the package cache file the caches sit next to.</param>
    /// <param name="settings">The settings that decide how long a cached profile is reused.</param>
    public static async Task LoadCachesAsync(ILogger logger, string cacheFilePath, AnalyzerSettings settings)
    {
        GitHubRepositoryRiskCache profiles = GetOrCreateRepositoryCache(logger);
        profiles.MaxAge = settings.RiskCacheMaxAge;
        profiles.IsRefreshForced = settings.RefreshRiskCache;

        await GetOrCreateResponseCache(logger).LoadAsync(GetResponseCacheFilePath(cacheFilePath));
        await profiles.LoadAsync(cacheFilePath);
    }

    /// <summary>
    /// Persists the responses and repository profiles seen during this run next to the package cache at
    /// <paramref name="cacheFilePath"/>.
    /// </summary>
    /// <param name="logger">The logger to report cache problems to.</param>
    /// <param name="cacheFilePath">The path of the package cache file the caches sit next to.</param>
    public static async Task SaveCachesAsync(ILogger logger, string cacheFilePath)
    {
        await GetOrCreateResponseCache(logger).SaveAsync(GetResponseCacheFilePath(cacheFilePath));
        await GetOrCreateRepositoryCache(logger).SaveAsync(cacheFilePath);
    }

    /// <summary>
    /// Returns the shared response cache, creating it on first use.
    /// </summary>
    private static GitHubResponseCache GetOrCreateResponseCache(ILogger logger)
    {
        lock (SharedLock)
        {
            return responseCache ??= new GitHubResponseCache(logger);
        }
    }

    /// <summary>
    /// Derives the response cache file path from the package cache file path.
    /// </summary>
    private static string GetResponseCacheFilePath(string cacheFilePath) =>
        cacheFilePath.ToPath().Directory / "github-responses.bin";
}
