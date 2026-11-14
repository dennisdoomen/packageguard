using System.Text.Json;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>
/// Collects the issue and pull request metrics of a repository, preferring the GraphQL API because it answers in a
/// single request what the REST API only exposes one issue and one pull request at a time.
/// </summary>
internal sealed class GitHubActivityCollector
{
    private readonly GitHubApiClient apiClient;
    private readonly GitHubIssueCollector issues;
    private readonly GitHubPullRequestCollector pullRequests;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    /// <param name="logger">The logger to report unreadable responses to.</param>
    public GitHubActivityCollector(GitHubApiClient apiClient, ILogger logger)
    {
        this.apiClient = apiClient;
        issues = new GitHubIssueCollector(apiClient);
        pullRequests = new GitHubPullRequestCollector(apiClient, logger);
    }

    /// <summary>Collects the issue and pull request metrics, through GraphQL when possible and REST otherwise.</summary>
    public async Task<GitHubActivityData> CollectAsync(GitHubRepositoryContext repository)
    {
        GitHubRepositoryActivity? activity = await TryGetActivityViaGraphQlAsync(repository);
        if (activity is not null)
        {
            return new GitHubActivityData(
                GitHubIssueCollector.BuildFromActivity(activity),
                GitHubPullRequestCollector.BuildFromActivity(activity));
        }

        Task<GitHubIssueData> issueDataTask = issues.CollectAsync(repository);
        Task<GitHubPullRequestQualityData> pullRequestDataTask = pullRequests.CollectAsync(repository);
        await Task.WhenAll(issueDataTask, pullRequestDataTask);

        return new GitHubActivityData(await issueDataTask, await pullRequestDataTask);
    }

    /// <summary>
    /// Runs the repository activity query, returning <see langword="null"/> when GraphQL is unavailable because no
    /// token is configured, or when the query did not come back with a repository.
    /// </summary>
    private async Task<GitHubRepositoryActivity?> TryGetActivityViaGraphQlAsync(GitHubRepositoryContext repository)
    {
        if (!apiClient.SupportsGraphQl)
        {
            return null;
        }

        Dictionary<string, object> variables = new()
        {
            ["owner"] = repository.OwnerLogin,
            ["name"] = repository.RepositoryName,
            ["issueSampleSize"] = GitHubIssueCollector.IssueResponseSampleSize,
            ["closedIssueSampleSize"] = GitHubIssueCollector.ClosedIssueSampleSize,
            ["pullRequestSampleSize"] = GitHubPullRequestCollector.PullRequestQualitySampleSize
        };

        using JsonDocument? data = await apiClient.PostGraphQlAsync(GitHubGraphQlQuery.RepositoryActivity, variables);
        return data is null ? null : GitHubRepositoryActivityReader.Read(data.RootElement);
    }
}
