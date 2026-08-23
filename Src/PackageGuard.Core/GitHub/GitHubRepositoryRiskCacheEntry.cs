using MemoryPack;

namespace PackageGuard.Core.GitHub;

/// <summary>
/// One repository's cached risk profile, together with when it was collected.
/// </summary>
[MemoryPackable]
internal sealed partial class GitHubRepositoryRiskCacheEntry
{
    /// <summary>
    /// The API root URL of the repository the profile describes.
    /// </summary>
    public required string RepositoryApiRoot { get; init; }

    /// <summary>
    /// The collected profile, or <see langword="null"/> when the repository could not be read.
    /// </summary>
    public GitHubRepositoryRiskData? Data { get; init; }

    /// <summary>
    /// The moment the profile was collected.
    /// </summary>
    public DateTimeOffset StoredAt { get; init; }

    /// <summary>
    /// Indicates whether the entry was read or written during the current run. Entries left untouched are dropped
    /// when the cache is persisted, so it does not keep repositories that are no longer referenced.
    /// </summary>
    [MemoryPackIgnore]
    public bool IsUsed { get; set; }
}
