namespace PackageGuard.Core.GitHub;

/// <summary>
/// The issue and pull request activity of a repository, as returned by a single GraphQL query.
/// </summary>
internal sealed class GitHubRepositoryActivity
{
    /// <summary>
    /// The total number of open issues labelled as a bug, which can exceed the number of sampled issues.
    /// </summary>
    public int OpenBugIssueCount { get; init; }

    /// <summary>
    /// The sampled open bug issues, newest first.
    /// </summary>
    public IReadOnlyList<GitHubActivityIssue> OpenBugIssues { get; init; } = [];

    /// <summary>
    /// The sampled recently closed bug issues, most recently updated first.
    /// </summary>
    public IReadOnlyList<GitHubActivityClosedIssue> ClosedBugIssues { get; init; } = [];

    /// <summary>
    /// The sampled closed pull requests, most recently updated first.
    /// </summary>
    public IReadOnlyList<GitHubActivityPullRequest> ClosedPullRequests { get; init; } = [];
}

/// <summary>
/// An open issue together with the comments needed to measure the maintainer response time.
/// </summary>
internal sealed class GitHubActivityIssue
{
    /// <summary>The moment the issue was opened.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The names of the labels on the issue.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>The comments on the issue, oldest first.</summary>
    public IReadOnlyList<GitHubActivityComment> Comments { get; init; } = [];
}

/// <summary>
/// A single issue comment.
/// </summary>
/// <param name="CreatedAt">The moment the comment was posted.</param>
/// <param name="AuthorAssociation">How the author relates to the repository, such as <c>OWNER</c> or <c>NONE</c>.</param>
internal sealed record GitHubActivityComment(DateTimeOffset CreatedAt, string? AuthorAssociation);

/// <summary>
/// A closed issue and whether it was ever reopened.
/// </summary>
/// <param name="ClosedAt">The moment the issue was closed.</param>
/// <param name="WasReopened">Indicates whether the issue carries a reopen event.</param>
internal sealed record GitHubActivityClosedIssue(DateTimeOffset? ClosedAt, bool WasReopened);

/// <summary>
/// A closed pull request together with its reviewers.
/// </summary>
internal sealed class GitHubActivityPullRequest
{
    /// <summary>The moment the pull request was merged, or <see langword="null"/> when it was closed unmerged.</summary>
    public DateTimeOffset? MergedAt { get; init; }

    /// <summary>The moment the pull request was opened.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>How the author relates to the repository, such as <c>OWNER</c> or <c>NONE</c>.</summary>
    public string? AuthorAssociation { get; init; }

    /// <summary>The logins of the people who reviewed the pull request.</summary>
    public IReadOnlyList<string> ReviewerLogins { get; init; } = [];

    /// <summary>Indicates whether the pull request was merged rather than closed unmerged.</summary>
    public bool IsMerged => MergedAt is not null;
}
