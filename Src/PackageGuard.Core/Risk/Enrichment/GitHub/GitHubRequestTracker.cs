using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Tells whether the GitHub requests behind a fetched value all got an answer.</summary>
internal static class GitHubRequestTracker
{
    /// <summary>
    /// Runs <paramref name="fetch"/> and tells whether every GitHub request it made got an answer, so that a signal
    /// can be left unknown instead of being read as absent.
    /// </summary>
    public static async Task<Tracked<T>> TrackAsync<T>(Func<Task<T>> fetch)
    {
        using GitHubRequestFailureScope failures = GitHubRequestFailureScope.Begin();

        T value = await fetch();
        return new Tracked<T>(value, !failures.HasFailures);
    }
}
