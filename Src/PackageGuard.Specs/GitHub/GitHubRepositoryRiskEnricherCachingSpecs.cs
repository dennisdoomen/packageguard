using System;
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

    private static PackageInfo CreatePackage(string name, string repositoryUrl = "https://github.com/acme/widget") =>
        new()
        {
            Name = name,
            Version = "1.0.0",
            Source = "NuGet",
            RepositoryUrl = repositoryUrl
        };
}
