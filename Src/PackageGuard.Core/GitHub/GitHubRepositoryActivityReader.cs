using System.Text.Json;

namespace PackageGuard.Core.GitHub;

/// <summary>
/// Turns the response of the <see cref="GitHubGraphQlQuery.RepositoryActivity"/> query into a
/// <see cref="GitHubRepositoryActivity"/>.
/// </summary>
internal static class GitHubRepositoryActivityReader
{
    /// <summary>
    /// Reads the repository activity from the <c>data</c> element of a GraphQL response, or returns
    /// <see langword="null"/> when the response holds no repository.
    /// </summary>
    /// <param name="data">The <c>data</c> element of the GraphQL response.</param>
    public static GitHubRepositoryActivity? Read(JsonElement data)
    {
        if (!data.TryGetProperty("repository", out JsonElement repository) ||
            repository.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new GitHubRepositoryActivity
        {
            OpenBugIssueCount = ReadTotalCount(repository, "openBugIssues"),
            OpenBugIssues = ReadNodes(repository, "openBugIssues").Select(ReadIssue).ToArray(),
            ClosedBugIssues = ReadNodes(repository, "closedBugIssues").Select(ReadClosedIssue).ToArray(),
            ClosedPullRequests = ReadNodes(repository, "pullRequests").Select(ReadPullRequest).ToArray()
        };
    }

    /// <summary>
    /// Reads the <c>totalCount</c> of a GraphQL connection, defaulting to zero when it is absent.
    /// </summary>
    private static int ReadTotalCount(JsonElement repository, string connectionName)
    {
        if (!repository.TryGetProperty(connectionName, out JsonElement connection) ||
            !connection.TryGetProperty("totalCount", out JsonElement totalCount))
        {
            return 0;
        }

        return totalCount.TryGetInt32(out int count) ? count : 0;
    }

    /// <summary>
    /// Enumerates the <c>nodes</c> of a GraphQL connection, skipping the nulls GraphQL can return for them.
    /// </summary>
    private static IEnumerable<JsonElement> ReadNodes(JsonElement owner, string connectionName)
    {
        if (!owner.TryGetProperty(connectionName, out JsonElement connection) ||
            !connection.TryGetProperty("nodes", out JsonElement nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return nodes.EnumerateArray().Where(node => node.ValueKind == JsonValueKind.Object);
    }

    /// <summary>
    /// Reads an open issue with its labels and comments.
    /// </summary>
    private static GitHubActivityIssue ReadIssue(JsonElement issue) => new()
    {
        CreatedAt = ReadDate(issue, "createdAt") ?? DateTimeOffset.UtcNow,
        Labels = ReadNodes(issue, "labels")
            .Select(label => label.TryGetProperty("name", out JsonElement name) ? name.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray(),
        Comments = ReadNodes(issue, "comments").Select(ReadComment).ToArray()
    };

    /// <summary>
    /// Reads a single issue comment.
    /// </summary>
    private static GitHubActivityComment ReadComment(JsonElement comment) => new(
        ReadDate(comment, "createdAt") ?? DateTimeOffset.UtcNow,
        comment.TryGetProperty("authorAssociation", out JsonElement association) ? association.GetString() : null);

    /// <summary>
    /// Reads a closed issue and whether its timeline holds a reopen event.
    /// </summary>
    private static GitHubActivityClosedIssue ReadClosedIssue(JsonElement issue) => new(
        ReadDate(issue, "closedAt"),
        ReadTotalCount(issue, "timelineItems") > 0);

    /// <summary>
    /// Reads a closed pull request with the logins of its reviewers.
    /// </summary>
    private static GitHubActivityPullRequest ReadPullRequest(JsonElement pullRequest) => new()
    {
        CreatedAt = ReadDate(pullRequest, "createdAt") ?? DateTimeOffset.UtcNow,
        MergedAt = ReadDate(pullRequest, "mergedAt"),
        AuthorAssociation = pullRequest.TryGetProperty("authorAssociation", out JsonElement association)
            ? association.GetString()
            : null,
        ReviewerLogins = ReadNodes(pullRequest, "reviews")
            .Select(ReadReviewerLogin)
            .Where(login => !string.IsNullOrWhiteSpace(login))
            .Select(login => login!)
            .ToArray()
    };

    /// <summary>
    /// Reads the login of a review's author, which is absent for deleted accounts.
    /// </summary>
    private static string? ReadReviewerLogin(JsonElement review)
    {
        if (!review.TryGetProperty("author", out JsonElement author) || author.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return author.TryGetProperty("login", out JsonElement login) ? login.GetString() : null;
    }

    /// <summary>
    /// Reads an ISO 8601 timestamp property, returning <see langword="null"/> when it is absent or unparsable.
    /// </summary>
    private static DateTimeOffset? ReadDate(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out DateTimeOffset parsed) ? parsed : null;
    }
}
