using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects whether a repository has a security policy and how well it describes vulnerability reporting.</summary>
internal sealed class GitHubSecurityPolicyCollector
{
    private readonly GitHubRepositoryFiles files;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubSecurityPolicyCollector(GitHubApiClient apiClient)
    {
        files = new GitHubRepositoryFiles(apiClient);
    }

    /// <summary>Reads SECURITY.md and checks detail/disclosure quality.</summary>
    public async Task<GitHubSecurityPolicyData> CollectAsync(GitHubRepositoryContext repository, string[] rootFiles)
    {
        string? securityPolicyPath = FindSecurityPolicyPath(rootFiles);
        if (securityPolicyPath is null)
        {
            return new GitHubSecurityPolicyData();
        }

        string content = await files.TryGetContentAsync(repository, securityPolicyPath);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new GitHubSecurityPolicyData();
        }

        string normalized = content.ToLowerInvariant();
        bool hasContact = normalized.Contains("security@") ||
                          normalized.Contains("contact") ||
                          normalized.Contains("report");

        bool hasPrivateChannel = normalized.Contains("private") ||
                                 normalized.Contains("gpg") ||
                                 normalized.Contains("pgp") ||
                                 normalized.Contains("encrypted");

        return new GitHubSecurityPolicyData
        {
            Exists = true,
            IsDetailed = content.Length >= 400 && hasContact,
            HasCoordinatedDisclosure = hasContact && hasPrivateChannel
        };
    }

    /// <summary>
    /// Returns the path of the repository's security policy, or <see langword="null"/> when the root listing shows
    /// there is none. GitHub also honours the policy under <c>.github</c>, but only probe for it when that directory
    /// exists: a request for a file that is not there costs the same against the rate limit as one that is.
    /// </summary>
    private static string? FindSecurityPolicyPath(string[] rootFiles)
    {
        string? rootPolicy = rootFiles.FirstOrDefault(file =>
            file.Equals("SECURITY.md", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("SECURITY", StringComparison.OrdinalIgnoreCase));

        if (rootPolicy is not null)
        {
            return rootPolicy;
        }

        return rootFiles.Contains(".github", StringComparer.OrdinalIgnoreCase) ? ".github/SECURITY.md" : null;
    }
}
