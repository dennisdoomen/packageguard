using System.Text.Json;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.GitHub;
using static PackageGuard.Core.Common.JsonElementReader;
using static PackageGuard.Core.Common.Statistics;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects how quickly pull requests get merged, who contributes them, and how many people review them.</summary>
internal sealed class GitHubPullRequestCollector
{
    /// <summary>
    /// The number of most recently closed pull requests the contribution and reviewer metrics are based on.
    /// </summary>
    internal const int PullRequestQualitySampleSize = 30;

    /// <summary>
    /// The number of pull requests whose reviews are read. Each one costs a request, and the reviewer counts stop
    /// moving well before the sample is exhausted.
    /// </summary>
    private const int ReviewSampleSize = 20;

    private readonly GitHubApiClient apiClient;
    private readonly ILogger logger;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    /// <param name="logger">The logger to report unreadable responses to.</param>
    public GitHubPullRequestCollector(GitHubApiClient apiClient, ILogger logger)
    {
        this.apiClient = apiClient;
        this.logger = logger;
    }

    /// <summary>
    /// Collects every pull request metric through the REST API, from a single listing of the repository's closed
    /// pull requests.
    /// </summary>
    public async Task<GitHubPullRequestQualityData> CollectAsync(GitHubRepositoryContext repository)
    {
        try
        {
            GitHubPullRequestSnapshot[] mergedPullRequests = await GetMergedPullRequestsAsync(repository);
            return await GetPullRequestQualityDataAsync(repository, mergedPullRequests);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            logger.LogDebug(ex, "Failed to read pull request data from {RepositoryApiRoot}", repository.ApiRoot);
            return new GitHubPullRequestQualityData();
        }
    }

    /// <summary>Builds the pull request metrics from the activity returned by the GraphQL query.</summary>
    public static GitHubPullRequestQualityData BuildFromActivity(GitHubRepositoryActivity activity)
    {
        GitHubActivityPullRequest[] merged = activity.ClosedPullRequests.Where(pr => pr.IsMerged).ToArray();
        double? medianMergeDays = ComputeMedian(merged
            .Select(pr => (pr.MergedAt!.Value - pr.CreatedAt).TotalDays)
            .ToList());

        GitHubActivityPullRequest[] sample = merged.Take(PullRequestQualitySampleSize).ToArray();
        if (sample.Length == 0)
        {
            return new GitHubPullRequestQualityData { MedianMergeDays = medianMergeDays };
        }

        int uniqueReviewerCount = sample
            .SelectMany(pr => pr.ReviewerLogins)
            .Where(login => !GitHubIdentity.IsBot(login))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new GitHubPullRequestQualityData
        {
            ExternalContributionRate =
                sample.Count(pr => GitHubIdentity.IsExternalAuthorAssociation(pr.AuthorAssociation)) / (double)sample.Length,
            UniqueReviewerCount = uniqueReviewerCount,
            ReviewerDiversityRatio = uniqueReviewerCount / (double)sample.Length,
            MedianMergeDays = medianMergeDays
        };
    }

    /// <summary>
    /// Reads the merged pull requests among the 100 most recently closed ones. Both the merge-time metric and the
    /// review metrics come out of this single response; they used to request the same endpoint twice, once with a
    /// page size of 100 and once with a page size of 30.
    /// </summary>
    private async Task<GitHubPullRequestSnapshot[]> GetMergedPullRequestsAsync(GitHubRepositoryContext repository)
    {
        using JsonDocument? pullsDoc = await apiClient.GetJsonAsync(
            $"{repository.ApiRoot}/pulls?state=closed&sort=updated&direction=desc&per_page=100",
            fields: GitHubResponseFields.Of("number", "author_association", "created_at", "merged_at"));

        if (pullsDoc?.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return pullsDoc.RootElement.EnumerateArray()
            .Select(ReadPullRequestSnapshot)
            .Where(snapshot => snapshot.IsMerged)
            .ToArray();
    }

    /// <summary>Reads the fields of a closed pull request that feed the merge-time and quality metrics.</summary>
    private static GitHubPullRequestSnapshot ReadPullRequestSnapshot(JsonElement pullRequest, int closedIndex)
    {
        DateTimeOffset? createdAt = TryReadDate(pullRequest, "created_at");
        DateTimeOffset? mergedAt = TryReadDate(pullRequest, "merged_at");

        return new GitHubPullRequestSnapshot
        {
            Number = pullRequest.TryGetProperty("number", out JsonElement numberElement) &&
                     numberElement.TryGetInt32(out int number)
                ? number
                : 0,
            AuthorAssociation = pullRequest.TryGetProperty("author_association", out JsonElement association)
                ? association.GetString()
                : null,
            IsMerged = pullRequest.TryGetProperty("merged_at", out JsonElement mergedAtElement) &&
                       mergedAtElement.ValueKind == JsonValueKind.String,
            ClosedIndex = closedIndex,
            MergeDays = createdAt.HasValue && mergedAt.HasValue
                ? (mergedAt.Value - createdAt.Value).TotalDays
                : null
        };
    }

    /// <summary>
    /// Computes the merge-time, external contribution, and reviewer diversity metrics from the pull requests that
    /// were already fetched.
    /// </summary>
    private async Task<GitHubPullRequestQualityData> GetPullRequestQualityDataAsync(GitHubRepositoryContext repository,
        GitHubPullRequestSnapshot[] mergedPullRequests)
    {
        double? medianMergeDays = ComputeMedian(mergedPullRequests
            .Where(pullRequest => pullRequest.MergeDays.HasValue)
            .Select(pullRequest => pullRequest.MergeDays!.Value)
            .ToList());

        GitHubPullRequestSnapshot[] sample = mergedPullRequests
            .Where(pullRequest => pullRequest is
            {
                Number: > 0, ClosedIndex: < PullRequestQualitySampleSize
            })
            .ToArray();

        if (sample.Length == 0)
        {
            return new GitHubPullRequestQualityData { MedianMergeDays = medianMergeDays };
        }

        int externalContributionCount = sample.Count(pullRequest =>
            GitHubIdentity.IsExternalAuthorAssociation(pullRequest.AuthorAssociation));

        int uniqueReviewerCount = await CountUniqueReviewersAsync(repository, sample);

        return new GitHubPullRequestQualityData
        {
            ExternalContributionRate = externalContributionCount / (double)sample.Length,
            UniqueReviewerCount = uniqueReviewerCount,
            ReviewerDiversityRatio = uniqueReviewerCount / (double)sample.Length,
            MedianMergeDays = medianMergeDays
        };
    }

    /// <summary>
    /// Counts the distinct reviewers across a bounded sample of pull requests, one request per pull request.
    /// </summary>
    private async Task<int> CountUniqueReviewersAsync(GitHubRepositoryContext repository,
        GitHubPullRequestSnapshot[] sample)
    {
        IEnumerable<Task<string[]>> reviewTasks = sample
            .Take(ReviewSampleSize)
            .Select(pullRequest => GetReviewerLoginsAsync(repository, pullRequest.Number));

        string[][] reviewResults = await Task.WhenAll(reviewTasks);
        return reviewResults.SelectMany(logins => logins).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    /// <summary>
    /// Returns unique non-bot reviewer logins for a pull request. This only refines a signal, so the request is
    /// dropped once the rate limit budget runs low.
    /// </summary>
    private async Task<string[]> GetReviewerLoginsAsync(GitHubRepositoryContext repository, int pullRequestNumber)
    {
        try
        {
            using JsonDocument? reviewsDoc = await apiClient.GetJsonAsync(
                $"{repository.ApiRoot}/pulls/{pullRequestNumber}/reviews?per_page=100",
                GitHubRequestImportance.Optional,
                GitHubResponseFields.Of("user.login"));

            if (reviewsDoc?.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return reviewsDoc.RootElement.EnumerateArray()
                .Where(review => review.TryGetProperty("user", out JsonElement userElement) &&
                                 userElement.TryGetProperty("login", out JsonElement loginElement) &&
                                 !string.IsNullOrWhiteSpace(loginElement.GetString()))
                .Select(review => review.GetProperty("user").GetProperty("login").GetString()!)
                .Where(login => !GitHubIdentity.IsBot(login))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}
