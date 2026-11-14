using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects the Dependabot configuration of a repository.</summary>
internal sealed class GitHubDependabotCollector
{
    private readonly GitHubRepositoryFiles files;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubDependabotCollector(GitHubApiClient apiClient)
    {
        files = new GitHubRepositoryFiles(apiClient);
    }

    /// <summary>
    /// Reads the Dependabot configuration, but only when the workflows do not already prove that dependency updates
    /// are automated. Most repositories do not have the file, and asking for it costs a request either way.
    /// </summary>
    public Task<string> GetConfigAsync(GitHubRepositoryContext repository, string workflowContent)
    {
        bool alreadyKnown = workflowContent.Contains("dependabot", StringComparison.OrdinalIgnoreCase) ||
                            workflowContent.Contains("renovate", StringComparison.OrdinalIgnoreCase);

        return alreadyKnown
            ? Task.FromResult(string.Empty)
            : files.TryGetContentAsync(repository, ".github/dependabot.yml");
    }
}
