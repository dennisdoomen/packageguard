using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Package;
using PackageGuard.Core.Risk.Enrichment;
using PackageGuard.Specs.Common;

namespace PackageGuard.Specs.GitHub;

[TestClass]
[DoNotParallelize]
public class GitHubRepositoryRiskEnricherCachingSpecs
{
    [TestInitialize]
    public void ClearSharedState() => GitHubRepositoryRiskEnricher.ClearCache();

    [TestCleanup]
    public void ClearSharedStateAfterwards() => GitHubRepositoryRiskEnricher.ClearCache();

    [TestMethod]
    public async Task Does_not_repeat_a_failed_repository_lookup_for_every_package_of_that_repository()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() =>
            ScriptedResponse.WithRateLimit(ScriptedResponse.NotFound(), remaining: 4000));

        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);

        // Act
        await enricher.EnrichAsync(CreatePackage("Acme.Widget.Core"));
        await enricher.EnrichAsync(CreatePackage("Acme.Widget.Abstractions"));
        await enricher.EnrichAsync(CreatePackage("Acme.Widget.Extensions"));

        // Assert
        handler.RequestCount.Should().Be(1,
            "the repository is only looked up once, however many packages point at it");
    }

    [TestMethod]
    public async Task Stops_looking_up_repositories_once_the_rate_limit_budget_is_spent()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() =>
            ScriptedResponse.PrimaryRateLimited(TimeSpan.FromMinutes(45)));

        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);

        // Act
        await enricher.EnrichAsync(CreatePackage("Acme.Widget.Core"));
        await enricher.EnrichAsync(CreatePackage("Contoso.Gadget", "https://github.com/contoso/gadget"));

        // Assert
        handler.RequestCount.Should().Be(1, "the second repository should not be attempted at all");
    }

    [TestMethod]
    public async Task Leaves_signals_unknown_and_flags_the_package_when_GitHub_stops_answering_halfway()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) => AnswerOnlyRepositoryAndOwner(request,
            () => ScriptedResponse.PrimaryRateLimited(TimeSpan.FromMinutes(45))));

        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var repositoryCache = new GitHubRepositoryRiskCache(NullLogger.Instance);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client, repositoryCache);
        PackageInfo package = CreatePackage("Acme.Widget");

        // Act
        await enricher.EnrichAsync(package);

        // Assert
        package.OwnerIsOrganization.Should().BeTrue();
        package.HasIncompleteGitHubRiskData.Should().BeTrue();
        package.HasGitHubRiskData.Should().BeFalse("the next run should try to collect the missing signals again");
        package.ContributorCount.Should().BeNull("an unanswered request does not mean there are no contributors");
        package.HasReadme.Should().BeNull();
        package.HasContributingGuide.Should().BeNull();
        package.HasSecurityPolicy.Should().BeNull();
        package.HasChangelog.Should().BeNull();
        package.HasTestSignal.Should().BeNull();
        package.WorkflowPlatformCount.Should().BeNull();

        repositoryCache.TryGet("https://api.github.com/repos/acme/widget", out _).Should()
            .BeFalse("an incomplete profile must not be cached");
    }

    [TestMethod]
    public async Task Treats_files_that_GitHub_reports_as_missing_as_absent()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            AnswerOnlyRepositoryAndOwner(request, ScriptedResponse.NotFound));

        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var repositoryCache = new GitHubRepositoryRiskCache(NullLogger.Instance);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client, repositoryCache);
        PackageInfo package = CreatePackage("Acme.Widget");

        // Act
        await enricher.EnrichAsync(package);

        // Assert
        package.HasIncompleteGitHubRiskData.Should().BeFalse();
        package.HasGitHubRiskData.Should().BeTrue();
        package.ContributorCount.Should().Be(0);
        package.HasReadme.Should().BeFalse();
        package.HasContributingGuide.Should().BeFalse();
        package.HasSecurityPolicy.Should().BeFalse();
        package.HasChangelog.Should().BeFalse();

        repositoryCache.TryGet("https://api.github.com/repos/acme/widget", out _).Should().BeTrue();
    }

    /// <summary>
    /// Answers the repository and owner lookups, and hands every other request to <paramref name="otherwise"/>.
    /// </summary>
    private static HttpResponseMessage AnswerOnlyRepositoryAndOwner(HttpRequestMessage request,
        Func<HttpResponseMessage> otherwise) =>
        request.RequestUri!.AbsoluteUri switch
        {
            "https://api.github.com/repos/acme/widget" => ScriptedResponse.Json(
                """
                {
                  "name": "widget",
                  "owner": { "login": "acme", "type": "Organization" },
                  "default_branch": "main",
                  "html_url": "https://github.com/acme/widget"
                }
                """),
            "https://api.github.com/orgs/acme" => ScriptedResponse.Json("""{ "created_at": "2015-01-01T00:00:00Z" }"""),
            _ => otherwise()
        };

    private static PackageInfo CreatePackage(string name, string repositoryUrl = "https://github.com/acme/widget") =>
        new()
        {
            Name = name,
            Version = "1.0.0",
            Source = "NuGet",
            RepositoryUrl = repositoryUrl
        };
}
