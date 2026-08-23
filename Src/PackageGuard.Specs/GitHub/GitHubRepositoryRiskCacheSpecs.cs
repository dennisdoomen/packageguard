using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Package;
using PackageGuard.Core.Risk.Enrichment;
using PackageGuard.Specs.Common;
using Pathy;

namespace PackageGuard.Specs.GitHub;

[TestClass]
[DoNotParallelize]
public class GitHubRepositoryRiskCacheSpecs
{
    [TestInitialize]
    public void ClearSharedState() => GitHubRepositoryRiskEnricher.ClearCache();

    [TestCleanup]
    public void ClearSharedStateAfterwards() => GitHubRepositoryRiskEnricher.ClearCache();

    [TestMethod]
    public async Task Reuses_a_repository_profile_that_a_previous_run_collected()
    {
        // Arrange
        string cachePath = CreateCachePath();
        var firstRunCache = new GitHubRepositoryRiskCache(NullLogger.Instance);
        var firstRunHandler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request));
        await EnrichAsync(firstRunHandler, firstRunCache, CreatePackage("Acme.Widget"));
        await firstRunCache.SaveAsync(cachePath);

        var laterRunCache = new GitHubRepositoryRiskCache(NullLogger.Instance);
        await laterRunCache.LoadAsync(cachePath);
        var laterRunHandler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request));
        PackageInfo package = CreatePackage("Acme.Widget");

        try
        {
            // Act
            GitHubRepositoryRiskEnricher.ClearCache();
            await EnrichAsync(laterRunHandler, laterRunCache, package);

            // Assert
            laterRunHandler.RequestCount.Should().Be(0, "the profile of the repository was already collected");
            package.HasGitHubRiskData.Should().BeTrue();
            package.OwnerIsOrganization.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(cachePath)!, recursive: true);
        }
    }

    [TestMethod]
    public async Task Collects_a_repository_profile_again_once_it_is_past_its_maximum_age()
    {
        // Arrange
        string cachePath = CreateCachePath();
        var firstRunCache = new GitHubRepositoryRiskCache(NullLogger.Instance);
        await EnrichAsync(new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request)),
            firstRunCache, CreatePackage("Acme.Widget"));

        await firstRunCache.SaveAsync(cachePath);

        var laterRunCache = new GitHubRepositoryRiskCache(NullLogger.Instance) { MaxAge = TimeSpan.Zero };
        await laterRunCache.LoadAsync(cachePath);
        var laterRunHandler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request));

        try
        {
            // Act
            GitHubRepositoryRiskEnricher.ClearCache();
            await EnrichAsync(laterRunHandler, laterRunCache, CreatePackage("Acme.Widget"));

            // Assert
            laterRunHandler.RequestCount.Should().BeGreaterThan(0, "the cached profile is too old to reuse");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(cachePath)!, recursive: true);
        }
    }

    [TestMethod]
    public async Task Describes_a_repository_once_however_many_packages_come_out_of_it()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request));
        var cache = new GitHubRepositoryRiskCache(NullLogger.Instance);
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client, cache);

        // Act
        await enricher.EnrichAsync(CreatePackage("Acme.Widget.Core"));
        int afterFirstPackage = handler.RequestCount;
        await enricher.EnrichAsync(CreatePackage("Acme.Widget.Abstractions"));
        await enricher.EnrichAsync(CreatePackage("Acme.Widget.Extensions"));

        // Assert
        handler.RequestCount.Should().Be(afterFirstPackage,
            "the profile is keyed by repository, not by the packages that come out of it");
    }

    private static async Task EnrichAsync(ScriptedHttpMessageHandler handler, GitHubRepositoryRiskCache cache,
        PackageInfo package)
    {
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var enricher = new GitHubRepositoryRiskEnricher(NullLogger.Instance, client, cache);
        await enricher.EnrichAsync(package);
    }

    private static string CreateCachePath() =>
        ChainablePath.Temp / $"packageguard-{Guid.NewGuid():N}" / "cache.bin";

    [TestMethod]
    public async Task Keeps_a_still_fresh_profile_that_this_run_had_no_use_for()
    {
        // Arrange
        string cachePath = CreateCachePath();
        var firstRunCache = new GitHubRepositoryRiskCache(NullLogger.Instance);
        await EnrichAsync(new ScriptedHttpMessageHandler((request, _) => FakeGitHub.Respond(request)),
            firstRunCache, CreatePackage("Acme.Widget"));

        await firstRunCache.SaveAsync(cachePath);

        var untouchedRunCache = new GitHubRepositoryRiskCache(NullLogger.Instance);
        await untouchedRunCache.LoadAsync(cachePath);

        try
        {
            // Act
            await untouchedRunCache.SaveAsync(cachePath);

            var laterRunCache = new GitHubRepositoryRiskCache(NullLogger.Instance);
            await laterRunCache.LoadAsync(cachePath);

            // Assert
            laterRunCache.TryGet("https://api.github.com/repos/acme/widget", out GitHubRepositoryRiskData data)
                .Should().BeTrue("a run that needed no profile should not throw the cache away");

            data.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(cachePath)!, recursive: true);
        }
    }

    private static PackageInfo CreatePackage(string name) =>
        new()
        {
            Name = name,
            Version = "1.0.0",
            Source = "NuGet",
            RepositoryUrl = "https://github.com/acme/widget"
        };
}
