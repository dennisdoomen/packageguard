namespace PackageGuard.Core.GitHub;

/// <summary>
/// The GraphQL query that collects, in a single request, the issue and pull request detail that the REST API only
/// exposes one issue and one pull request at a time.
/// </summary>
internal static class GitHubGraphQlQuery
{
    /// <summary>
    /// Requests the open bug issues with their first comments, the recently closed bug issues with their reopen
    /// events, and the recently merged pull requests with their reviews.
    /// </summary>
    /// <remarks>
    /// Over REST the same data costs one request for each issue's comments, each closed issue's timeline, and each
    /// pull request's reviews: well over a hundred requests for an active repository. GraphQL charges a single
    /// request against a separate 5000-point-per-hour budget.
    /// </remarks>
    public const string RepositoryActivity =
        """
        query RepositoryActivity(
            $owner: String!,
            $name: String!,
            $issueSampleSize: Int!,
            $closedIssueSampleSize: Int!,
            $pullRequestSampleSize: Int!) {
          repository(owner: $owner, name: $name) {
            openBugIssues: issues(
                states: OPEN,
                labels: ["bug"],
                first: $issueSampleSize,
                orderBy: {field: CREATED_AT, direction: DESC}) {
              totalCount
              nodes {
                number
                createdAt
                labels(first: 20) { nodes { name } }
                comments(first: 30) {
                  nodes {
                    createdAt
                    authorAssociation
                  }
                }
              }
            }
            closedBugIssues: issues(
                states: CLOSED,
                labels: ["bug"],
                first: $closedIssueSampleSize,
                orderBy: {field: UPDATED_AT, direction: DESC}) {
              nodes {
                number
                closedAt
                timelineItems(itemTypes: [REOPENED_EVENT], first: 1) {
                  totalCount
                }
              }
            }
            pullRequests(
                states: [MERGED, CLOSED],
                first: 100,
                orderBy: {field: UPDATED_AT, direction: DESC}) {
              nodes {
                number
                createdAt
                mergedAt
                authorAssociation
                reviews(first: 50) {
                  nodes {
                    author { login }
                  }
                }
              }
            }
          }
        }
        """;
}
