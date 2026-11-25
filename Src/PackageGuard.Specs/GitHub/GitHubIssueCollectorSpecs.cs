using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Risk.Enrichment.GitHub;
using PackageGuard.Specs.Common;

namespace PackageGuard.Specs.GitHub;

[TestClass]
public class GitHubIssueCollectorSpecs
{
    private static readonly GitHubRepositoryContext Repository =
        new("https://api.github.com/repos/acme/widget", "main", "acme", "widget", true, "", null);

    [TestMethod]
    public async Task Unavailable_comments_and_closed_issues_do_not_become_negative_results()
    {
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/comments", StringComparison.Ordinal) ||
            request.RequestUri.ToString().Contains("/issues?state=closed")
                ? ScriptedResponse.NotFound()
                : FakeGitHub.Respond(request, 5));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        using GitHubRequestFailureScope scope = GitHubRequestFailureScope.Begin();

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        scope.HasFailures.Should().BeFalse();
        result.OpenBugIssueCount.Should().Be(5);
        result.IssueResponseCoverage.Should().BeNull();
        result.TriageWithinSevenDaysRate.Should().BeNull();
        result.ClosedBugIssueCountLast90Days.Should().BeNull();
        result.ReopenedBugIssueCountLast90Days.Should().BeNull();
    }

    [TestMethod]
    public async Task Missing_issue_endpoint_leaves_counts_unknown_without_marking_the_request_as_failed()
    {
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(ScriptedResponse.NotFound);
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        using GitHubRequestFailureScope scope = GitHubRequestFailureScope.Begin();

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        scope.HasFailures.Should().BeFalse();
        result.OpenBugIssueCount.Should().BeNull();
        result.ClosedBugIssueCountLast90Days.Should().BeNull();
    }

    [TestMethod]
    public async Task Missing_issue_timeline_leaves_reopened_count_unknown()
    {
        string closedAt = DateTimeOffset.UtcNow.ToString("O");
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.ToString().Contains("state=open")
                ? ScriptedResponse.Json("[]")
                : request.RequestUri.ToString().Contains("state=closed")
                    ? ScriptedResponse.Json($$"""[{"number":1,"closed_at":"{{closedAt}}"}]""")
                    : ScriptedResponse.NotFound());
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        using GitHubRequestFailureScope scope = GitHubRequestFailureScope.Begin();

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        scope.HasFailures.Should().BeFalse();
        result.ClosedBugIssueCountLast90Days.Should().Be(1);
        result.ReopenedBugIssueCountLast90Days.Should().BeNull();
    }

    [TestMethod]
    public async Task Forbidden_issue_endpoint_marks_the_request_as_failed_and_leaves_counts_unknown()
    {
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() =>
            ScriptedResponse.WithRateLimit(new HttpResponseMessage(HttpStatusCode.Forbidden), 4000));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        using GitHubRequestFailureScope scope = GitHubRequestFailureScope.Begin();

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        scope.HasFailures.Should().BeTrue();
        result.OpenBugIssueCount.Should().BeNull();
    }

    [TestMethod]
    public async Task Malformed_issue_listing_marks_the_request_as_failed()
    {
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json("{}"));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        using GitHubRequestFailureScope scope = GitHubRequestFailureScope.Begin();

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        scope.HasFailures.Should().BeTrue();
        result.OpenBugIssueCount.Should().BeNull();
    }

    [TestMethod]
    public async Task Small_open_issue_sample_does_not_produce_response_rates()
    {
        var handler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request, 1));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        result.OpenBugIssueCount.Should().Be(1);
        result.IssueResponseCoverage.Should().BeNull();
        result.TriageWithinSevenDaysRate.Should().BeNull();
    }

    [TestMethod]
    public async Task Finds_the_first_maintainer_response_on_a_later_comment_page()
    {
        string firstPage = ManyEvents("""{"author_association":"NONE","created_at":"2026-01-01T12:00:00Z"}""");
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/comments", StringComparison.Ordinal)
                ? ScriptedResponse.Json(request.RequestUri.Query.Contains("page=2")
                    ? """[{"author_association":"MEMBER","created_at":"2026-01-02T00:00:00Z"}]"""
                    : firstPage)
                : FakeGitHub.Respond(request, 5));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        result.IssueResponseCoverage.Should().Be(1);
        result.MedianIssueResponseDays.Should().Be(1);
        result.TriageWithinSevenDaysRate.Should().Be(1);
    }

    [TestMethod]
    public async Task Finds_reopen_events_on_a_later_timeline_page()
    {
        string closedAt = DateTimeOffset.UtcNow.ToString("O");
        string firstPage = ManyEvents("""{"event":"labeled"}""");
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.ToString().Contains("state=closed")
                ? ScriptedResponse.Json($$"""[{"number":1,"closed_at":"{{closedAt}}"}]""")
                : request.RequestUri.AbsolutePath.EndsWith("/timeline", StringComparison.Ordinal)
                    ? ScriptedResponse.Json(request.RequestUri.Query.Contains("page=2")
                        ? """[{"event":"reopened"}]"""
                        : firstPage)
                    : FakeGitHub.Respond(request, 0));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        result.ReopenedBugIssueCountLast90Days.Should().Be(1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_later_pages_leave_issue_metrics_unknown(bool timeline)
    {
        string closedAt = DateTimeOffset.UtcNow.ToString("O");
        string firstPage = timeline ? ManyEvents("""{"event":"labeled"}""")
            : ManyEvents("""{"author_association":"NONE","created_at":"2026-01-01T12:00:00Z"}""");
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith(timeline ? "/timeline" : "/comments", StringComparison.Ordinal)
                ? request.RequestUri.Query.Contains("page=2") ? ScriptedResponse.NotFound() : ScriptedResponse.Json(firstPage)
                : timeline && request.RequestUri.ToString().Contains("state=closed")
                    ? ScriptedResponse.Json($$"""[{"number":1,"closed_at":"{{closedAt}}"}]""")
                    : FakeGitHub.Respond(request, 5));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        if (timeline)
        {
            result.ReopenedBugIssueCountLast90Days.Should().BeNull();
        }
        else
        {
            result.IssueResponseCoverage.Should().BeNull();
            result.TriageWithinSevenDaysRate.Should().BeNull();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Issue_page_limit_leaves_the_affected_metric_unknown(bool timeline)
    {
        string closedAt = DateTimeOffset.UtcNow.ToString("O");
        string fullPage = timeline ? ManyEvents("""{"event":"labeled"}""")
            : ManyEvents("""{"author_association":"NONE","created_at":"2026-01-01T12:00:00Z"}""");
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith(timeline ? "/timeline" : "/comments", StringComparison.Ordinal)
                ? ScriptedResponse.Json(fullPage)
                : timeline && request.RequestUri.ToString().Contains("state=closed")
                    ? ScriptedResponse.Json($$"""[{"number":1,"closed_at":"{{closedAt}}"}]""")
                : FakeGitHub.Respond(request, 5));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        using GitHubRequestFailureScope scope = GitHubRequestFailureScope.Begin();

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        if (timeline)
        {
            result.ReopenedBugIssueCountLast90Days.Should().BeNull();
            handler.RequestedUrls.Count(url => url.Contains("/timeline?", StringComparison.Ordinal)).Should().Be(10);
        }
        else
        {
            result.IssueResponseCoverage.Should().BeNull();
            handler.RequestedUrls.Count(url => url.Contains("/comments?", StringComparison.Ordinal)).Should().Be(50);
        }

        scope.HasFailures.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Malformed_issue_events_leave_the_affected_metric_unknown(bool timeline)
    {
        string closedAt = DateTimeOffset.UtcNow.ToString("O");
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith(timeline ? "/timeline" : "/comments", StringComparison.Ordinal)
                ? ScriptedResponse.Json("[{}]")
                : timeline && request.RequestUri.ToString().Contains("state=closed")
                    ? ScriptedResponse.Json($$"""[{"number":1,"closed_at":"{{closedAt}}"}]""")
                    : FakeGitHub.Respond(request, 5));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        using GitHubRequestFailureScope scope = GitHubRequestFailureScope.Begin();

        GitHubIssueData result = await new GitHubIssueCollector(client).CollectAsync(Repository);

        if (timeline)
        {
            result.ReopenedBugIssueCountLast90Days.Should().BeNull();
        }
        else
        {
            result.IssueResponseCoverage.Should().BeNull();
        }

        scope.HasFailures.Should().BeTrue();
    }

    private static string ManyEvents(string item) => "[" + string.Join(",", Enumerable.Repeat(item, 100)) + "]";

    [TestMethod]
    public void Truncated_graphql_comments_do_not_count_as_unanswered()
    {
        var activity = new GitHubRepositoryActivity
        {
            OpenBugIssueCount = 5,
            OpenBugIssues = Enumerable.Range(0, 5)
                .Select(_ => new GitHubActivityIssue { CreatedAt = DateTimeOffset.UtcNow, AreCommentsComplete = false })
                .ToArray()
        };

        GitHubIssueData result = GitHubIssueCollector.BuildFromActivity(activity);

        result.IssueResponseCoverage.Should().BeNull();
        result.TriageWithinSevenDaysRate.Should().BeNull();
    }
}
