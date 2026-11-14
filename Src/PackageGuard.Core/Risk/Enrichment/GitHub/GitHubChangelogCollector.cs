using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects whether a repository has a changelog, whether it is boilerplate, and when it last changed.</summary>
internal sealed class GitHubChangelogCollector
{
    private readonly GitHubRepositoryFiles files;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubChangelogCollector(GitHubApiClient apiClient)
    {
        files = new GitHubRepositoryFiles(apiClient);
    }

    /// <summary>Reads CHANGELOG file and checks if it looks like a boilerplate.</summary>
    public async Task<GitHubChangelogData> CollectAsync(GitHubRepositoryContext repository, string[] rootFiles)
    {
        string? changelogFile = rootFiles.FirstOrDefault(IsChangelogFile);

        if (string.IsNullOrWhiteSpace(changelogFile))
        {
            return new GitHubChangelogData();
        }

        string content = await files.TryGetContentAsync(repository, changelogFile);
        return new GitHubChangelogData
        {
            Exists = true,
            IsDefault = LooksLikeBoilerplate(content)
        };
    }

    /// <summary>
    /// Returns the date of the most recent commit to the changelog in the repository root, or
    /// <see langword="null"/> when the root listing shows no changelog.
    /// </summary>
    public Task<DateTimeOffset?> GetUpdatedAtAsync(GitHubRepositoryContext repository, string[] rootFiles)
    {
        return rootFiles.Any(IsChangelogFile)
            ? files.TryGetLatestCommitDateAsync(repository, rootFiles.First(IsChangelogFile))
            : Task.FromResult<DateTimeOffset?>(null);
    }

    /// <summary>Returns true if the filename is a known changelog filename.</summary>
    private static bool IsChangelogFile(string file) =>
        file.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
        file.Equals("CHANGELOG", StringComparison.OrdinalIgnoreCase) ||
        file.Equals("RELEASE_NOTES.md", StringComparison.OrdinalIgnoreCase) ||
        file.Equals("NEWS.md", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true if the content appears to be a boilerplate changelog.</summary>
    private static bool LooksLikeBoilerplate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        string normalized = content.ToLowerInvariant();
        return content.Length < 200 ||
               normalized.Contains("coming soon") ||
               normalized.Contains("todo");
    }
}
