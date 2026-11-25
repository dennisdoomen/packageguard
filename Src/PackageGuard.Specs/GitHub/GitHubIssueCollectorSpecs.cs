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
            request.RequestUri!.ToString().EndsWith("/comments", StringComparison.Ordinal) ||
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
