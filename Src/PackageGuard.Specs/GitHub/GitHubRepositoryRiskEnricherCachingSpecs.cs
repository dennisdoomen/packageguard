using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using MemoryPack;
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
    public async Task Unanswered_requests_mark_the_package_as_incomplete_with_unknown_signals()
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
        package.OpenBugIssueCount.Should().BeNull("a missing issue endpoint does not reveal the issue count");
        package.ClosedBugIssueCountLast90Days.Should().BeNull();
        package.IssueResponseCoverage.Should().BeNull();

        repositoryCache.TryGet("https://api.github.com/repos/acme/widget", out _).Should().BeTrue();
    }

    [TestMethod]
    public async Task Refreshes_legacy_package_signals_and_clears_them_when_the_repository_is_unavailable()
    {
        var oldPackage = CreatePackage("Acme.Widget");
        oldPackage.HasGitHubRiskData = true;
        oldPackage.HasTestSignal = false;
        oldPackage.WorkflowPlatformCount = 0;
        oldPackage.OpenBugIssueCount = 0;
        oldPackage.HasReadme = false;
        oldPackage.HasVerifiedReleaseSignature = false;
        oldPackage.HasOsvRiskData = true;

        byte[] serialized = MemoryPackSerializer.Serialize(oldPackage);
        byte[] legacyBytes = serialized[..^sizeof(int)];
        legacyBytes[0]--;
        PackageInfo package = MemoryPackSerializer.Deserialize<PackageInfo>(legacyBytes);
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(ScriptedResponse.NotFound);
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);

        enricher.HasCachedData(package).Should().BeFalse();
        await enricher.EnrichAsync(package);

        package.HasGitHubRiskData.Should().BeFalse();
        package.HasTestSignal.Should().BeNull();
        package.WorkflowPlatformCount.Should().BeNull();
        package.OpenBugIssueCount.Should().BeNull();
        package.HasReadme.Should().BeNull();
        package.HasVerifiedReleaseSignature.Should().BeNull();
        package.HasOsvRiskData.Should().BeTrue();
    }

    [TestMethod]
    public async Task Reuses_current_package_signals_after_serialization()
    {
        var handler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);
        PackageInfo package = CreatePackage("Acme.Widget");
        await enricher.EnrichAsync(package);

        PackageInfo reloaded = MemoryPackSerializer.Deserialize<PackageInfo>(MemoryPackSerializer.Serialize(package));

        enricher.HasCachedData(reloaded).Should().BeTrue();
    }

    [TestMethod]
    public async Task Clears_current_signals_after_package_metadata_is_invalidated()
    {
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(ScriptedResponse.NotFound);
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client);
        PackageInfo package = CreatePackage("Acme.Widget");
        package.GitHubRiskDataVersion = GitHubRepositoryRiskCache.CurrentRiskDataVersion;
        package.HasGitHubRiskData = true;
        package.HasReadme = false;
        package.InvalidateCachedMetadata();

        await enricher.EnrichAsync(package);

        package.HasReadme.Should().BeNull();
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
