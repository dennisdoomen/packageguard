using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using PackageGuard.Core;
using Pathy;

namespace PackageGuard.Specs;

[TestClass]
public class PackageInfoSpecs
{
    [TestMethod]
    public void Can_match_on_case_insensitive_name_only()
    {
        // Arrange
        var package = new PackageInfo
        {
            Name = "Bogus",
            Version = "1.0.0",
        };

        // Act
        bool isMatch = package.SatisfiesRange("bogus", "1.0.0");

        // Assert
        isMatch.Should().BeTrue();
    }

    [TestMethod]
    public void Can_match_without_version()
    {
        // Arrange
        var package = new PackageInfo
        {
            Name = "Bogus",
            Version = "1.0.0",
        };

        // Act
        bool isMatch = package.SatisfiesRange("Bugos");

        // Assert
        isMatch.Should().BeFalse();
    }

    [TestMethod]
    public void An_exact_version_must_match()
    {
        // Arrange
        var package = new PackageInfo
        {
            Name = "Bogus",
            Version = "2.0.0",
        };

        // Act
        bool isMatch = package.SatisfiesRange("Bugos", "2.0.1");

        // Assert
        isMatch.Should().BeFalse();
    }

    [TestMethod]
    public void An_exact_version_matches()
    {
        // Arrange
        var package = new PackageInfo
        {
            Name = "Bogus",
            Version = "2.0.1",
        };

        // Act
        bool isMatch = package.SatisfiesRange("Bugos", "2.0.1");

        // Assert
        isMatch.Should().BeFalse();
    }

    [TestMethod]
    public void Can_use_an_exclusive_range()
    {
        // Arrange
        var package = new PackageInfo
        {
            Name = "Bogus",
            Version = "2.0.0",
        };

        // Act
        bool isMatch = package.SatisfiesRange("Bugos", "[1.0.0,2.0.0)");

        // Assert
        isMatch.Should().BeFalse();
    }

    [TestMethod]
    public void Can_use_an_inclusive_range()
    {
        // Arrange
        var package = new PackageInfo
        {
            Name = "Bogus",
            Version = "2.0.0",
        };

        // Act
        bool isMatch = package.SatisfiesRange("Bogus", "[1.0.0,2.0.0]");

        // Assert
        isMatch.Should().BeTrue();
    }

    [TestMethod]
    public async Task Loading_the_cache_keeps_the_collection_empty()
    {
        // Arrange
        var priorCollection = new PackageInfoCollection(NullLogger.Instance)
        {
            new PackageInfo
            {
                Name = "Bogus",
                Version = "2.0.0",
            }
        };

        await priorCollection.WriteToCache(ChainablePath.Current / "cache.bin");

        var currentCollection = new PackageInfoCollection(NullLogger.Instance);

        // Act
        await currentCollection.TryInitializeFromCache(ChainablePath.Current / "cache.bin");

        // Assert
        currentCollection.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Can_fetch_information_from_the_cache()
    {
        // Arrange
        var source = new SourceRepository(new PackageSource("https://nuget.org"), Array.Empty<INuGetResourceProvider>());

        var priorCollection = new PackageInfoCollection(NullLogger.Instance)
        {
            new PackageInfo
            {
                Name = "Bogus",
                Version = "2.0.0",
                SourceUrl = source.PackageSource.Source,
            }
        };

        await priorCollection.WriteToCache(ChainablePath.Current / "cache.bin");

        // Act
        var currentCollection = new PackageInfoCollection(NullLogger.Instance);
        await currentCollection.TryInitializeFromCache(ChainablePath.Current / "cache.bin");

        // Assert
        PackageInfo package = currentCollection.Find("Bogus", "2.0.0", [source]);
        package.Should().NotBeNull();

        currentCollection.Should().Contain(x => x.IsUsed && x.Name == "Bogus" && x.Version == "2.0.0");
    }

    [TestMethod]
    public async Task An_unused_package_is_removed_from_the_cache()
    {
        // Arrange
        var source = new SourceRepository(new PackageSource("https://nuget.org"), Array.Empty<INuGetResourceProvider>());

        var existingPackage = new PackageInfo
        {
            Name = "Bogus",
            Version = "2.0.0",
            SourceUrl = source.PackageSource.Source,
        };

        var priorCollection = new PackageInfoCollection(NullLogger.Instance)
        {
            existingPackage
        };

        existingPackage.IsUsed = false;

        await priorCollection.WriteToCache(ChainablePath.Current / "cache.bin");

        // Act
        var currentCollection = new PackageInfoCollection(NullLogger.Instance);
        await currentCollection.TryInitializeFromCache(ChainablePath.Current / "cache.bin");

        // Assert
        PackageInfo package = currentCollection.Find("Bogus", "2.0.0", [source]);
        package.Should().BeNull();
    }

    [TestMethod]
    public async Task Can_fetch_enriched_risk_information_from_the_cache()
    {
        // Arrange
        var source = new SourceRepository(new PackageSource("https://nuget.org"), Array.Empty<INuGetResourceProvider>());

        var priorCollection = new PackageInfoCollection(NullLogger.Instance)
        {
            new PackageInfo
            {
                Name = "Bogus",
                Version = "2.0.0",
                SourceUrl = source.PackageSource.Source,
                HasValidatedLicenseUrl = true,
                HasValidLicenseUrl = true,
                VulnerabilityCount = 2,
                MaxVulnerabilitySeverity = 8.2,
                HasOsvRiskData = true,
                IsPackageSigned = true,
                HasTrustedPackageSignature = true,
                HasSigningRiskData = true,
                ContributorCount = 5,
                HasGitHubRiskData = true,
                HasReadme = true,
                HasSecurityPolicy = true,
                SupportedTargetFrameworks = ["net8.0"],
                HasModernTargetFrameworkSupport = true,
                HasNativeBinaryAssets = false,
                DownloadCount = 1234,
                LatestStableVersion = "2.0.1"
            }
        };

        await priorCollection.WriteToCache(ChainablePath.Current / "cache.bin");

        // Act
        var currentCollection = new PackageInfoCollection(NullLogger.Instance);
        await currentCollection.TryInitializeFromCache(ChainablePath.Current / "cache.bin");
        PackageInfo package = currentCollection.Find("Bogus", "2.0.0", [source]);

        // Assert
        package.Should().NotBeNull();
        package.HasValidatedLicenseUrl.Should().BeTrue();
        package.HasValidLicenseUrl.Should().BeTrue();
        package.VulnerabilityCount.Should().Be(2);
        package.MaxVulnerabilitySeverity.Should().Be(8.2);
        package.HasOsvRiskData.Should().BeTrue();
        package.IsPackageSigned.Should().BeTrue();
        package.HasTrustedPackageSignature.Should().BeTrue();
        package.HasSigningRiskData.Should().BeTrue();
        package.ContributorCount.Should().Be(5);
        package.HasGitHubRiskData.Should().BeTrue();
        package.HasReadme.Should().BeTrue();
        package.HasSecurityPolicy.Should().BeTrue();
        package.SupportedTargetFrameworks.Should().ContainSingle().Which.Should().Be("net8.0");
        package.HasModernTargetFrameworkSupport.Should().BeTrue();
        package.HasNativeBinaryAssets.Should().BeFalse();
        package.DownloadCount.Should().Be(1234);
        package.LatestStableVersion.Should().Be("2.0.1");
    }

    [TestMethod]
    public async Task Report_risk_ignores_stale_cached_packages()
    {
        // Arrange
        var source = new SourceRepository(new PackageSource("https://nuget.org"), Array.Empty<INuGetResourceProvider>());
        string cachePath = ChainablePath.Current / "cache.bin";

        PackageInfo[] cachedPackages =
        [
            new PackageInfo
            {
                Name = "Bogus",
                Version = "2.0.0",
                SourceUrl = source.PackageSource.Source,
                CacheUpdatedAt = DateTimeOffset.UtcNow.AddHours(-30)
            }
        ];

        await using (FileStream fileStream = new(cachePath, FileMode.Create, FileAccess.Write))
        {
            await MemoryPackSerializer.SerializeAsync(fileStream, cachedPackages);
        }

        // Act
        var currentCollection = new PackageInfoCollection(NullLogger.Instance, new AnalyzerSettings
        {
            ReportRisk = true,
            RiskCacheMaxAge = TimeSpan.FromHours(24)
        });
        await currentCollection.TryInitializeFromCache(cachePath);
        PackageInfo package = currentCollection.Find("Bogus", "2.0.0", [source]);

        // Assert
        package.Should().BeNull();
    }

    [TestMethod]
    public async Task Report_risk_can_force_refresh_of_cached_packages()
    {
        // Arrange
        var source = new SourceRepository(new PackageSource("https://nuget.org"), Array.Empty<INuGetResourceProvider>());

        var priorCollection = new PackageInfoCollection(NullLogger.Instance)
        {
            new PackageInfo
            {
                Name = "Bogus",
                Version = "2.0.0",
                SourceUrl = source.PackageSource.Source
            }
        };

        await priorCollection.WriteToCache(ChainablePath.Current / "cache.bin");

        // Act
        var currentCollection = new PackageInfoCollection(NullLogger.Instance, new AnalyzerSettings
        {
            ReportRisk = true,
            RefreshRiskCache = true
        });
        await currentCollection.TryInitializeFromCache(ChainablePath.Current / "cache.bin");
        PackageInfo package = currentCollection.Find("Bogus", "2.0.0", [source]);

        // Assert
        package.Should().BeNull();
    }
}
