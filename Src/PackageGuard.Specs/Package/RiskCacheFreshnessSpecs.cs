using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using PackageGuard.Core;
using PackageGuard.Core.Package;
using Pathy;

namespace PackageGuard.Specs.Package;

[TestClass]
public class RiskCacheFreshnessSpecs
{
    private static readonly SourceRepository Source =
        new(new PackageSource("https://nuget.org"), Array.Empty<INuGetResourceProvider>());

    [TestMethod]
    public async Task Reuses_a_cache_entry_that_is_still_within_its_maximum_age()
    {
        // Arrange
        string cachePath = CreateCachePath();
        await WriteCacheAsync(cachePath, CreateEnrichedPackage(cachedAt: DateTimeOffset.UtcNow.AddHours(-2)));

        var collection = new PackageInfoCollection(NullLogger.Instance, CreateSettings());
        await collection.TryInitializeFromCache(cachePath);

        // Act
        PackageInfo package = collection.Find("Bogus", "2.0.0", [Source]);

        // Assert
        package.Should().NotBeNull();
        package.HasGitHubRiskData.Should().BeTrue("the entry is fresh, so no signal has to be collected again");
    }

    [TestMethod]
    public async Task Drops_the_risk_signals_of_a_cache_entry_that_is_past_its_maximum_age()
    {
        // Arrange
        string cachePath = CreateCachePath();
        await WriteCacheAsync(cachePath, CreateEnrichedPackage(cachedAt: DateTimeOffset.UtcNow.AddDays(-3)));

        var collection = new PackageInfoCollection(NullLogger.Instance, CreateSettings());
        await collection.TryInitializeFromCache(cachePath);

        // Act
        PackageInfo staleLookup = collection.Find("Bogus", "2.0.0", [Source]);
        PackageInfo refreshed = collection.Add(CreateFreshlyFetchedPackage());

        // Assert
        staleLookup.Should().BeNull("a stale entry has to be fetched again");
        refreshed.HasGitHubRiskData.Should().BeFalse("the repository signals have to be collected again");
        refreshed.HasOsvRiskData.Should().BeFalse("the vulnerability signals have to be collected again");
        refreshed.HasValidatedLicenseUrl.Should().BeFalse("the license URL has to be validated again");
    }

    [TestMethod]
    public async Task Replaces_the_registry_metadata_of_a_stale_entry_with_what_was_just_fetched()
    {
        // Arrange
        string cachePath = CreateCachePath();
        await WriteCacheAsync(cachePath, CreateEnrichedPackage(cachedAt: DateTimeOffset.UtcNow.AddDays(-3)));

        var collection = new PackageInfoCollection(NullLogger.Instance, CreateSettings());
        await collection.TryInitializeFromCache(cachePath);
        collection.Find("Bogus", "2.0.0", [Source]);

        // Act
        PackageInfo refreshed = collection.Add(CreateFreshlyFetchedPackage());

        // Assert
        refreshed.LatestStableVersion.Should().Be("3.0.0", "a newer version was released since the entry was written");
        refreshed.DownloadCount.Should().Be(9999);
        refreshed.IsMajorVersionBehindLatest.Should().BeTrue();
    }

    [TestMethod]
    public async Task Stamps_a_refreshed_entry_so_that_it_does_not_stay_stale_for_good()
    {
        // Arrange
        string cachePath = CreateCachePath();
        await WriteCacheAsync(cachePath, CreateEnrichedPackage(cachedAt: DateTimeOffset.UtcNow.AddDays(-3)));

        var firstRun = new PackageInfoCollection(NullLogger.Instance, CreateSettings());
        await firstRun.TryInitializeFromCache(cachePath);
        firstRun.Find("Bogus", "2.0.0", [Source]);
        firstRun.Add(CreateFreshlyFetchedPackage());
        await firstRun.WriteToCache(cachePath);

        var secondRun = new PackageInfoCollection(NullLogger.Instance, CreateSettings());
        await secondRun.TryInitializeFromCache(cachePath);

        // Act
        PackageInfo package = secondRun.Find("Bogus", "2.0.0", [Source]);

        // Assert
        package.Should().NotBeNull("the entry was refreshed during the previous run, so it is fresh again");
    }

    [TestMethod]
    public async Task Keeps_reusing_the_cache_when_risk_reporting_is_off()
    {
        // Arrange
        string cachePath = CreateCachePath();
        await WriteCacheAsync(cachePath, CreateEnrichedPackage(cachedAt: DateTimeOffset.UtcNow.AddYears(-1)));

        var collection = new PackageInfoCollection(NullLogger.Instance, new AnalyzerSettings { ReportRisk = false });
        await collection.TryInitializeFromCache(cachePath);

        // Act
        PackageInfo package = collection.Find("Bogus", "2.0.0", [Source]);

        // Assert
        package.Should().NotBeNull("without risk reporting the age of an entry does not matter");
    }

    private static AnalyzerSettings CreateSettings() => new()
    {
        ReportRisk = true,
        RiskCacheMaxAge = TimeSpan.FromHours(24)
    };

    private static string CreateCachePath() =>
        ChainablePath.Temp / $"packageguard-{Guid.NewGuid():N}" / "cache.bin";

    private static async Task WriteCacheAsync(string cachePath, PackageInfo package)
    {
        var collection = new PackageInfoCollection(NullLogger.Instance) { package };
        await collection.WriteToCache(cachePath);
    }

    private static PackageInfo CreateEnrichedPackage(DateTimeOffset cachedAt) => new()
    {
        Name = "Bogus",
        Version = "2.0.0",
        Source = "nuget.org",
        SourceUrl = Source.PackageSource.Source,
        License = "MIT",
        CacheUpdatedAt = cachedAt,
        HasGitHubRiskData = true,
        HasOsvRiskData = true,
        HasValidatedLicenseUrl = true,
        ContributorCount = 5,
        DownloadCount = 1234,
        LatestStableVersion = "2.0.1"
    };

    private static PackageInfo CreateFreshlyFetchedPackage() => new()
    {
        Name = "Bogus",
        Version = "2.0.0",
        Source = "nuget.org",
        SourceUrl = Source.PackageSource.Source,
        License = "MIT",
        DownloadCount = 9999,
        LatestStableVersion = "3.0.0",
        IsMajorVersionBehindLatest = true
    };
}
