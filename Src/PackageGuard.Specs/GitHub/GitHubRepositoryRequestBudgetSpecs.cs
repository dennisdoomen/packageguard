using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.CSharp.FetchingStrategies;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Package;
using PackageGuard.Core.Risk.Enrichment;
using PackageGuard.Specs.Common;

namespace PackageGuard.Specs.GitHub;

[TestClass]
[DoNotParallelize]
public class GitHubRepositoryRequestBudgetSpecs
{
    [TestInitialize]
    public void ClearSharedState() => GitHubRepositoryRiskEnricher.ClearCache();

    [TestCleanup]
    public void ClearSharedStateAfterwards() => GitHubRepositoryRiskEnricher.ClearCache();

    [TestMethod]
    public async Task Lists_the_closed_pull_requests_only_once()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request));
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);

        // Act
        await enricher.EnrichAsync(CreatePackage());

        // Assert
        handler.RequestedUrls.Where(url => url.Contains("/pulls?")).Should().HaveCount(1,
            "the merge-time and review metrics come out of the same listing");
    }

    [TestMethod]
    public async Task Does_not_probe_for_a_security_policy_that_the_root_listing_rules_out()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request));
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);

        // Act
        await enricher.EnrichAsync(CreatePackage());

        // Assert
        handler.RequestedUrls.Should().NotContain(url => url.Contains("SECURITY"),
            "the repository root holds neither SECURITY.md nor a .github directory");
    }

    [TestMethod]
    public async Task Reads_the_comments_of_at_most_a_sample_of_the_open_bug_issues()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request, openBugIssueCount: 80));
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);

        // Act
        await enricher.EnrichAsync(CreatePackage());

        // Assert
        handler.RequestedUrls.Where(url => url.EndsWith("/comments", StringComparison.Ordinal))
            .Should().HaveCount(20, "reading every one of the 80 issues would cost 80 requests");
    }

    [TestMethod]
    public async Task Reads_the_repository_license_and_its_risk_metadata_from_a_single_response()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request));
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var licenseFetcher = new GitHubLicenseFetcher(NullLogger.Instance, client);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);
        PackageInfo package = CreatePackage();

        // Act
        await licenseFetcher.FetchLicenseAsync(package);
        await enricher.EnrichAsync(package);

        // Assert
        package.License.Should().Be("MIT");
        handler.RequestedUrls.Count(url => url == "https://api.github.com/repos/acme/widget").Should().Be(1);
    }

    [TestMethod]
    public async Task Looks_up_the_repository_of_several_packages_from_the_same_repository_only_once()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request));
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var licenseFetcher = new GitHubLicenseFetcher(NullLogger.Instance, client);

        // Act
        await licenseFetcher.FetchLicenseAsync(CreatePackage("Acme.Widget.Core"));
        await licenseFetcher.FetchLicenseAsync(CreatePackage("Acme.Widget.Abstractions"));
        await licenseFetcher.FetchLicenseAsync(CreatePackage("Acme.Widget.Extensions"));

        // Assert
        handler.RequestCount.Should().Be(1, "all three packages share one repository");
    }

    [TestMethod]
    public async Task Collects_the_issue_and_review_detail_in_a_single_graphql_request_when_a_token_is_configured()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/graphql"
                ? ScriptedResponse.Json(FakeGitHub.RepositoryActivity)
                : FakeGitHub.Respond(request, openBugIssueCount: 80));

        using var client = new GitHubApiClient(NullLogger.Instance, "secret-token", responseCache: null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);

        // Act
        await enricher.EnrichAsync(CreatePackage());

        // Assert
        handler.RequestedUrls.Should().ContainSingle(url => url.EndsWith("/graphql", StringComparison.Ordinal));
        handler.RequestedUrls.Should().NotContain(url => url.EndsWith("/comments", StringComparison.Ordinal));
        handler.RequestedUrls.Should().NotContain(url => url.Contains("/reviews?"));
        handler.RequestedUrls.Should().NotContain(url => url.Contains("/timeline?"));
        handler.RequestedUrls.Should().NotContain(url => url.Contains("/pulls?"));
        handler.RequestedUrls.Should().NotContain(url => url.Contains("/issues?"));
    }

    [TestMethod]
    public async Task Falls_back_to_the_rest_api_when_the_graphql_query_fails()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/graphql"
                ? ScriptedResponse.Json("""{"errors":[{"message":"Bad credentials"}]}""")
                : FakeGitHub.Respond(request));

        using var client = new GitHubApiClient(NullLogger.Instance, "secret-token", responseCache: null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);

        // Act
        await enricher.EnrichAsync(CreatePackage());

        // Assert
        handler.RequestedUrls.Should().Contain(url => url.Contains("/issues?state=open"));
        handler.RequestedUrls.Should().Contain(url => url.Contains("/pulls?"));
    }

    private static PackageInfo CreatePackage(string name = "Acme.Widget") =>
        new()
        {
            Name = name,
            Version = "1.0.0",
            Source = "NuGet",
            RepositoryUrl = "https://github.com/acme/widget"
        };
}

/// <summary>
/// Answers the GitHub REST endpoints the risk enricher reads, with just enough shape to be parsed.
/// </summary>
internal static class FakeGitHub
{
    /// <summary>
    /// Returns a plausible response for the given request, or a 404 for anything the enricher should not ask for.
    /// </summary>
    public static HttpResponseMessage Respond(HttpRequestMessage request, int openBugIssueCount = 3)
    {
        string url = request.RequestUri!.ToString();
        string body = FindBody(url, openBugIssueCount);

        return body is null ? ScriptedResponse.NotFound() : ScriptedResponse.Json(body);
    }

    private static string FindBody(string url, int openBugIssueCount) => url switch
    {
        "https://api.github.com/repos/acme/widget" => Repository,
        "https://api.github.com/orgs/acme" => """{"created_at":"2015-01-01T00:00:00Z"}""",
        _ when url.Contains("/contents?ref=") => RootContents,
        _ when url.Contains("/issues?state=open") => OpenIssues(openBugIssueCount),
        _ when url.EndsWith("/comments", StringComparison.Ordinal) => "[]",
        _ when url.Contains("/issues?state=closed") => "[]",
        _ when url.Contains("/pulls?") => ClosedPullRequests,
        _ when url.Contains("/reviews?") => "[]",
        _ when url.Contains("/contributors") => """[{"login":"maintainer","contributions":50}]""",
        _ when url.Contains("/commits?") => "[]",
        _ when url.Contains("/releases") => "[]",
        _ when url.Contains("/actions/runs") => """{"workflow_runs":[]}""",
        _ when url.Contains("/branches/") => """{"protected":true}""",
        _ when url.Contains("/readme") => """{"content":""}""",
        _ => null
    };

    /// <summary>
    /// A GraphQL response carrying the issue and pull request activity of the fake repository.
    /// </summary>
    public const string RepositoryActivity =
        """
        {
          "data": {
            "repository": {
              "openBugIssues": {
                "totalCount": 80,
                "nodes": [
                  {
                    "number": 1,
                    "createdAt": "2026-01-01T00:00:00Z",
                    "labels": {"nodes": [{"name": "bug"}]},
                    "comments": {"nodes": [{"createdAt": "2026-01-02T00:00:00Z", "authorAssociation": "OWNER"}]}
                  }
                ]
              },
              "closedBugIssues": {"nodes": []},
              "pullRequests": {
                "nodes": [
                  {
                    "number": 9,
                    "createdAt": "2026-01-01T00:00:00Z",
                    "mergedAt": "2026-01-03T00:00:00Z",
                    "authorAssociation": "OWNER",
                    "reviews": {"nodes": [{"author": {"login": "reviewer"}}]}
                  }
                ]
              }
            }
          }
        }
        """;

    private const string Repository =
        """
        {
          "name": "widget",
          "default_branch": "main",
          "html_url": "https://github.com/acme/widget",
          "owner": {"login": "acme", "type": "Organization"},
          "license": {"spdx_id": "MIT"}
        }
        """;

    private const string RootContents =
        """
        [
          {"name": "README.md"},
          {"name": "src"}
        ]
        """;

    private const string ClosedPullRequests =
        """
        [
          {"number": 9, "created_at": "2026-01-01T00:00:00Z", "merged_at": "2026-01-03T00:00:00Z",
           "author_association": "OWNER"}
        ]
        """;

    private static string OpenIssues(int count) =>
        "[" + string.Join(",", Enumerable.Range(1, count).Select(number =>
            $$"""
              {"number": {{number}}, "created_at": "2026-01-01T00:00:00Z", "labels": [],
               "comments_url": "https://api.github.com/repos/acme/widget/issues/{{number}}/comments"}
              """)) + "]";
}
