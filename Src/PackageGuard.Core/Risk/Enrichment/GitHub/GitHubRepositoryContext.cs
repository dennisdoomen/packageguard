namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>The details of a GitHub repository and its owner that the signal collectors need.</summary>
/// <param name="ApiRoot">The GitHub API URL of the repository, such as <c>https://api.github.com/repos/owner/name</c>.</param>
/// <param name="DefaultBranch">The name of the repository's default branch.</param>
/// <param name="OwnerLogin">The login of the user or organization that owns the repository.</param>
/// <param name="RepositoryName">The name of the repository, without its owner.</param>
/// <param name="OwnerIsOrganization">Indicates whether the repository is owned by an organization.</param>
/// <param name="CanonicalUrl">The URL GitHub reports for the repository, which follows renames and transfers.</param>
/// <param name="OwnerCreatedAt">The date the owner's account was created, if known.</param>
internal sealed record GitHubRepositoryContext(
    string ApiRoot,
    string DefaultBranch,
    string OwnerLogin,
    string RepositoryName,
    bool OwnerIsOrganization,
    string CanonicalUrl,
    DateTimeOffset? OwnerCreatedAt)
{
    /// <summary>The default branch, escaped for use in a URL.</summary>
    public string EscapedDefaultBranch => Uri.EscapeDataString(DefaultBranch);
}
