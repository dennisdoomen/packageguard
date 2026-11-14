using System.Text.Json;
using PackageGuard.Core.GitHub;
using static PackageGuard.Core.Common.JsonElementReader;
using static PackageGuard.Core.Common.Statistics;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects how many bug issues a repository has and how quickly its maintainers respond to them.</summary>
internal sealed class GitHubIssueCollector
{
    /// <summary>
    /// The number of open bug issues whose comments are read to measure maintainer response time. Each one costs a
    /// request, and a median over this many issues is as informative as one over a hundred.
    /// </summary>
    internal const int IssueResponseSampleSize = 20;

    /// <summary>
    /// The number of recently closed bug issues that are listed, before they are narrowed to the last 90 days.
    /// </summary>
    internal const int ClosedIssueSampleSize = 50;

    /// <summary>
    /// The number of recently closed bug issues whose timeline is read to spot reopenings.
    /// </summary>
    private const int ReopenedIssueSampleSize = 20;

    private readonly GitHubApiClient apiClient;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubIssueCollector(GitHubApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    /// <summary>Fetches open and closed bug issues through the REST API and computes issue health metrics.</summary>
    public async Task<GitHubIssueData> CollectAsync(GitHubRepositoryContext repository)
    {
        using JsonDocument? issuesDoc = await apiClient.GetJsonAsync(
            $"{repository.ApiRoot}/issues?state=open&labels=bug&per_page=100",
            fields: GitHubResponseFields.Of("number", "created_at", "labels.name", "comments_url", "pull_request.url"));

        if (issuesDoc?.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new GitHubIssueData();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        GitHubIssueSnapshot[] openIssues = ReadOpenIssues(issuesDoc.RootElement, now);
        GitHubIssueResponsiveness responsiveness = await MeasureIssueResponsivenessAsync(openIssues);

        GitHubClosedIssueSnapshot[] closedIssues = await GetClosedBugIssuesAsync(repository);
        int reopenedBugCount = await CountReopenedIssuesAsync(repository,
            closedIssues.Take(ReopenedIssueSampleSize).ToArray());

        return new GitHubIssueData
        {
            OpenBugIssueCount = openIssues.Length,
            StaleCriticalBugIssueCount =
                openIssues.Count(issue => issue.IsCritical && issue.CreatedAt < now.AddMonths(-6)),
            MedianIssueResponseDays = responsiveness.MedianResponseDays,
            MedianCriticalIssueResponseDays = responsiveness.MedianCriticalResponseDays,
            IssueResponseCoverage = responsiveness.ResponseCoverage,
            MedianOpenBugAgeDays = ComputeMedian(openIssues.Select(issue => (now - issue.CreatedAt).TotalDays).ToList()),
            ClosedBugIssueCountLast90Days = closedIssues.Length,
            ReopenedBugIssueCountLast90Days = reopenedBugCount,
            TriageWithinSevenDaysRate = responsiveness.TriageWithinSevenDaysRate
        };
    }

    /// <summary>Builds the issue health metrics from the activity returned by the GraphQL query.</summary>
    public static GitHubIssueData BuildFromActivity(GitHubRepositoryActivity activity)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GitHubIssueSnapshot[] openIssues = activity.OpenBugIssues
            .Select(issue => new GitHubIssueSnapshot
            {
                CreatedAt = issue.CreatedAt,
                IsCritical = issue.Labels.Any(IsCriticalLabelName)
            })
            .ToArray();

        GitHubIssueResponseData[] responses = activity.OpenBugIssues
            .Select(MeasureResponse)
            .ToArray();

        GitHubActivityClosedIssue[] closedIssues = activity.ClosedBugIssues
            .Where(issue => issue.ClosedAt >= now.AddDays(-90))
            .ToArray();

        GitHubIssueResponsiveness responsiveness = openIssues.Length == 0
            ? new GitHubIssueResponsiveness()
            : BuildResponsiveness(openIssues, responses);

        return new GitHubIssueData
        {
            OpenBugIssueCount = activity.OpenBugIssueCount,
            StaleCriticalBugIssueCount =
                openIssues.Count(issue => issue.IsCritical && issue.CreatedAt < now.AddMonths(-6)),
            MedianIssueResponseDays = responsiveness.MedianResponseDays,
            MedianCriticalIssueResponseDays = responsiveness.MedianCriticalResponseDays,
            IssueResponseCoverage = responsiveness.ResponseCoverage,
            MedianOpenBugAgeDays = ComputeMedian(openIssues.Select(issue => (now - issue.CreatedAt).TotalDays).ToList()),
            ClosedBugIssueCountLast90Days = closedIssues.Length,
            ReopenedBugIssueCountLast90Days =
                closedIssues.Take(ReopenedIssueSampleSize).Count(issue => issue.WasReopened),
            TriageWithinSevenDaysRate = responsiveness.TriageWithinSevenDaysRate
        };
    }

    /// <summary>Finds the first maintainer comment on an issue and turns it into a response time.</summary>
    private static GitHubIssueResponseData MeasureResponse(GitHubActivityIssue issue)
    {
        DateTimeOffset? firstMaintainerComment = issue.Comments
            .Where(comment => GitHubIdentity.IsMaintainerAssociation(comment.AuthorAssociation))
            .Select(comment => comment.CreatedAt)
            .OrderBy(createdAt => createdAt)
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();

        return firstMaintainerComment is null
            ? new GitHubIssueResponseData()
            : new GitHubIssueResponseData
            {
                HasMaintainerResponse = true,
                ResponseDays = (firstMaintainerComment.Value - issue.CreatedAt).TotalDays
            };
    }

    /// <summary>Reads the open bug issues out of an issue listing, leaving out the pull requests GitHub mixes in.</summary>
    private static GitHubIssueSnapshot[] ReadOpenIssues(JsonElement issues, DateTimeOffset now) =>
        issues.EnumerateArray()
            .Where(issue => !issue.TryGetProperty("pull_request", out _))
            .Select(issue => new GitHubIssueSnapshot
            {
                Number = ReadNumber(issue),
                CreatedAt = TryReadDate(issue, "created_at") ?? now,
                IsCritical = issue.TryGetProperty("labels", out JsonElement labels) &&
                             labels.ValueKind == JsonValueKind.Array &&
                             labels.EnumerateArray().Any(IsCriticalLabel),
                CommentsUrl = issue.TryGetProperty("comments_url", out JsonElement commentsElement)
                    ? commentsElement.GetString()
                    : null
            })
            .ToArray();

    /// <summary>Reads the issue number, or zero when it is missing.</summary>
    private static int ReadNumber(JsonElement issue) =>
        issue.TryGetProperty("number", out JsonElement numberElement) && numberElement.TryGetInt32(out int number)
            ? number
            : 0;

    /// <summary>
    /// Measures how quickly maintainers respond, from the comments on a bounded sample of the open bug issues. Reading
    /// the comments costs one request per issue, so the sample is capped; the rates are relative to the sample.
    /// </summary>
    private async Task<GitHubIssueResponsiveness> MeasureIssueResponsivenessAsync(GitHubIssueSnapshot[] openIssues)
    {
        GitHubIssueSnapshot[] sample = openIssues.Take(IssueResponseSampleSize).ToArray();
        if (sample.Length == 0)
        {
            return new GitHubIssueResponsiveness();
        }

        GitHubIssueResponseData[] responses =
            await Task.WhenAll(sample.Select(TryGetIssueResponseDataAsync));

        return BuildResponsiveness(sample, responses);
    }

    /// <summary>Turns the per-issue response measurements into the aggregate issue responsiveness metrics.</summary>
    private static GitHubIssueResponsiveness BuildResponsiveness(GitHubIssueSnapshot[] sample,
        GitHubIssueResponseData[] responses)
    {
        List<double> responseDays = responses
            .Where(response => response.ResponseDays.HasValue)
            .Select(response => response.ResponseDays!.Value)
            .ToList();

        List<double> criticalResponseDays = sample.Zip(responses)
            .Where(pair => pair.First.IsCritical && pair.Second.ResponseDays.HasValue)
            .Select(pair => pair.Second.ResponseDays!.Value)
            .ToList();

        return new GitHubIssueResponsiveness
        {
            MedianResponseDays = ComputeMedian(responseDays),
            MedianCriticalResponseDays = ComputeMedian(criticalResponseDays),
            ResponseCoverage = responses.Count(response => response.HasMaintainerResponse) / (double)sample.Length,
            TriageWithinSevenDaysRate =
                responses.Count(response => response.ResponseDays is <= 7) / (double)sample.Length
        };
    }

    /// <summary>
    /// Fetches the first maintainer comment on an issue to compute response time. This only refines a signal, so the
    /// request is dropped once the rate limit budget runs low.
    /// </summary>
    private async Task<GitHubIssueResponseData> TryGetIssueResponseDataAsync(GitHubIssueSnapshot issue)
    {
        if (string.IsNullOrWhiteSpace(issue.CommentsUrl))
        {
            return new GitHubIssueResponseData();
        }

        try
        {
            using JsonDocument? commentsDoc = await apiClient.GetJsonAsync(issue.CommentsUrl,
                GitHubRequestImportance.Optional, GitHubResponseFields.Of("author_association", "created_at"));
            if (commentsDoc?.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new GitHubIssueResponseData();
            }

            DateTimeOffset? firstMaintainerComment = commentsDoc.RootElement.EnumerateArray()
                .Where(comment => comment.TryGetProperty("author_association", out JsonElement associationElement) &&
                                  GitHubIdentity.IsMaintainerAssociation(associationElement.GetString()))
                .Select(comment => TryReadDate(comment, "created_at"))
                .Where(date => date.HasValue)
                .OrderBy(date => date)
                .FirstOrDefault();

            return firstMaintainerComment != null
                ? new GitHubIssueResponseData
                {
                    HasMaintainerResponse = true,
                    ResponseDays = (firstMaintainerComment.Value - issue.CreatedAt).TotalDays
                }
                : new GitHubIssueResponseData();
        }
        catch
        {
            return new GitHubIssueResponseData();
        }
    }

    /// <summary>Fetches bug issues closed in the last 90 days.</summary>
    /// <remarks>
    /// The issues are filtered by their closing date here instead of through the <c>since</c> parameter, for the
    /// same reason as <see cref="GitHubCommitHealthCollector.CollectAsync"/>. An issue closed in the last 90 days was
    /// also updated in that period, so the most recently updated issues contain the same matches.
    /// </remarks>
    private async Task<GitHubClosedIssueSnapshot[]> GetClosedBugIssuesAsync(GitHubRepositoryContext repository)
    {
        try
        {
            using JsonDocument? issuesDoc = await apiClient.GetJsonAsync(
                $"{repository.ApiRoot}/issues?state=closed&labels=bug&sort=updated&direction=desc&per_page=50",
                fields: GitHubResponseFields.Of("number", "closed_at", "pull_request.url"));

            if (issuesDoc?.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return issuesDoc.RootElement.EnumerateArray()
                .Where(issue => !issue.TryGetProperty("pull_request", out _))
                .Select(issue => new GitHubClosedIssueSnapshot
                {
                    Number = ReadNumber(issue),
                    ClosedAt = TryReadDate(issue, "closed_at")
                })
                .Where(issue => issue is { Number: > 0, ClosedAt: not null } &&
                                issue.ClosedAt >= DateTimeOffset.UtcNow.AddDays(-90))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Counts how many of the given closed issues were subsequently reopened.</summary>
    private async Task<int> CountReopenedIssuesAsync(GitHubRepositoryContext repository,
        GitHubClosedIssueSnapshot[] closedIssues)
    {
        if (closedIssues.Length == 0)
        {
            return 0;
        }

        using var throttler = new SemaphoreSlim(6);
        Task<bool>[] tasks = closedIssues.Select(async issue =>
        {
            await throttler.WaitAsync();
            try
            {
                return await WasReopenedAsync(repository, issue);
            }
            catch
            {
                return false;
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        bool[] reopened = await Task.WhenAll(tasks);
        return reopened.Count(value => value);
    }

    /// <summary>
    /// Reads the timeline of a closed issue to tell whether it was reopened. This only refines a signal, so the
    /// request is dropped once the rate limit budget runs low.
    /// </summary>
    private async Task<bool> WasReopenedAsync(GitHubRepositoryContext repository, GitHubClosedIssueSnapshot issue)
    {
        using JsonDocument? eventsDoc = await apiClient.GetJsonAsync(
            $"{repository.ApiRoot}/issues/{issue.Number}/timeline?per_page=100",
            GitHubRequestImportance.Optional,
            GitHubResponseFields.Of("event"));

        return eventsDoc?.RootElement.ValueKind == JsonValueKind.Array &&
               eventsDoc.RootElement.EnumerateArray().Any(eventItem =>
                   string.Equals(
                       eventItem.TryGetProperty("event", out JsonElement eventElement) ? eventElement.GetString() : null,
                       "reopened", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns true if the issue label name indicates critical severity or a security issue.</summary>
    private static bool IsCriticalLabel(JsonElement label) => IsCriticalLabelName(
        label.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty);

    /// <summary>Returns true if a label name marks an issue as critical.</summary>
    private static bool IsCriticalLabelName(string name) =>
        name.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("security", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("sev1", StringComparison.OrdinalIgnoreCase);
}
