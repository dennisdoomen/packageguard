using MemoryPack;

namespace PackageGuard.Core.GitHub;

/// <summary>
/// A single cached GitHub API response, used to revalidate the resource with a conditional request instead of
/// downloading it again.
/// </summary>
[MemoryPackable]
internal sealed partial class GitHubResponseCacheEntry
{
    /// <summary>
    /// The absolute request URL this entry was stored for.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// The entity tag returned with the response, replayed as <c>If-None-Match</c> on the next request.
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>
    /// The response body, or an empty string when the response carried no content.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Indicates that the resource did not exist when it was last requested.
    /// </summary>
    public bool IsNotFound { get; set; }

    /// <summary>
    /// The moment the entry was last revalidated against the API.
    /// </summary>
    public DateTimeOffset StoredAt { get; set; }

    /// <summary>
    /// Indicates whether the entry was read or written during the current run. Entries left untouched are dropped
    /// when the cache is persisted, so the file does not grow without bound.
    /// </summary>
    [MemoryPackIgnore]
    public bool IsUsed { get; set; }
}
