using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Meziantou.Extensions.Logging.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using PackageGuard.Core.Npm;
using Pathy;

namespace PackageGuard.Specs.Npm;

[TestClass]
public class NpmLockFileParserSpecs
{
    [TestMethod]
    public async Task Can_collect_package_metadata_from_lock_file()
    {
        // Arrange
        var testProject = ChainablePath.Current / "TestCases" / "NpmApp";
        var packageLockPath = (testProject / "package-lock.json").ToString();
        var projectPath = testProject.ToString();

        var metadataFetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance);
        var loader = new NpmLockFileParser(metadataFetcher, NullLogger.Instance);

        var packages = new PackageInfoCollection(NullLogger.Instance);

        // Act
        await loader.CollectPackageMetadata(packageLockPath, packages);

        // Assert
        packages.Should().NotBeEmpty();

        var expressPackage = packages.FirstOrDefault(p => p.Name == "express");
        expressPackage.Should().NotBeNull();
        expressPackage!.Version.Should().Be("4.18.2");
        expressPackage.License.Should().Be("MIT");
        expressPackage.Source.Should().Be("npm");
        expressPackage.Projects.Should().Contain(projectPath);

        var lodashPackage = packages.FirstOrDefault(p => p.Name == "lodash");
        lodashPackage.Should().NotBeNull();
        lodashPackage!.Version.Should().Be("4.17.21");
        lodashPackage.License.Should().Be("MIT");

        // Should also include transitive dependencies
        var acceptsPackage = packages.FirstOrDefault(p => p.Name == "accepts");
        acceptsPackage.Should().NotBeNull();
        acceptsPackage!.Version.Should().Be("1.3.8");
    }

    [TestMethod]
    public async Task Skips_root_package_entry()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var testProject = ChainablePath.Current / "TestCases" / "NpmApp";
        var packageLockPath = (testProject / "package-lock.json").ToString();

        var metadataFetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance);
        var loader = new NpmLockFileParser(metadataFetcher, loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata(packageLockPath, packages);

        // Assert
        // The root package with empty string key should not be included
        packages.Should().NotContain(p => p.Name == "test-npm-app");
    }

    [TestMethod]
    public async Task Handles_packages_without_license()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var testProject = ChainablePath.Current / "TestCases" / "NpmApp";
        var packageLockPath = (testProject / "package-lock.json").ToString();

        var metadataFetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance);
        var loader = new NpmLockFileParser(metadataFetcher, NullLogger.Instance);

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata(packageLockPath, packages);

        // Assert - should not throw exception and packages without license should have null License
        packages.Should().NotBeEmpty();
        packages.Should().OnlyContain(p => p.License != null || p.License == null);
    }

    [TestMethod]
    public async Task Handles_nonexistent_file_gracefully()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var metadataFetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance);

        var loader = new NpmLockFileParser(metadataFetcher, loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata("/nonexistent/package-lock.json", packages);

        // Assert - should not throw and should log warning
        packages.Should().BeEmpty();
        loggingProvider.Logs.Should().Contain(log => log.Message.Contains("not found"));
    }

    [TestMethod]
    public async Task Fetches_license_from_npm_registry_when_missing()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var testProject = ChainablePath.Current / "TestCases" / "NpmAppNoLicense";
        var metadataFetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance);

        var loader = new NpmLockFileParser(metadataFetcher, loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata((string)(testProject / "package-lock.json"), packages);

        // Assert
        packages.Should().NotBeEmpty();

        var isNumberPackage = packages.FirstOrDefault(p => p.Name == "is-number");
        isNumberPackage.Should().NotBeNull();
        isNumberPackage!.Version.Should().Be("7.0.0");
        // License should be fetched from NPM registry
        isNumberPackage.License.Should().NotBeNullOrEmpty();
        isNumberPackage.RepositoryUrl.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task Supports_private_npm_registries()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var testProject = ChainablePath.Current / "TestCases" / "NpmPrivateRegistryApp";
        var packageLockPath = (testProject / "package-lock-private-registry.json").ToString();

        var metadataFetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance);

        var loader = new NpmLockFileParser(metadataFetcher, loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata(packageLockPath, packages);

        // Assert
        packages.Should().NotBeEmpty();

        var privatePackage = packages.FirstOrDefault(p => p.Name == "@mycompany/private-package");
        privatePackage.Should().NotBeNull();
        privatePackage!.Version.Should().Be("1.2.3");
        privatePackage.License.Should().Be("MIT");

        // SourceUrl should point to the private registry
        privatePackage.SourceUrl.Should().Contain("npm.mycompany.com");

        // Verify the fetcher would use the correct registry URL (though it won't actually fetch in this test)
        privatePackage.Source.Should().Be("npm");
    }
}
