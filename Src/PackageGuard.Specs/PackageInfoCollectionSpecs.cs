using System;
using System.Threading.Tasks;
using FluentAssertions;
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
}
