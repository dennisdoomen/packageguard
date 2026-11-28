using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Meziantou.Extensions.Logging.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using PackageGuard.Core.Npm;
using PackageGuard.Core.Package;
using PackageGuard.Specs.Common;
using Pathy;

namespace PackageGuard.Specs.Npm;

[TestClass]
public class PnpmLockFileParserSpecs
{
    [TestMethod]
    public async Task Can_collect_package_metadata_from_pnpm_lock_file()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var testProject = ChainablePath.Current / "TestCases" / "PnpmApp";
        var pnpmLockPath = (testProject / "pnpm-lock.yaml").ToString();
        var projectPath = testProject.ToString();

        var loader = new PnpmLockFileParser(loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata(pnpmLockPath, packages);

        // Assert
        packages.Should().NotBeEmpty();

        var expressPackage = packages.FirstOrDefault(p => p.Name == "express");
        expressPackage.Should().NotBeNull();
        expressPackage!.Version.Should().Be("4.22.1");
        expressPackage.Source.Should().Be("npm");
        expressPackage.Projects.Should().Contain(projectPath);

        var lodashPackage = packages.FirstOrDefault(p => p.Name == "lodash");
        lodashPackage.Should().NotBeNull();
        lodashPackage!.Version.Should().Be("4.18.1");
    }

    [TestMethod]
    public async Task Handles_nonexistent_pnpm_lock_file_gracefully()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var loader = new PnpmLockFileParser(loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata("/nonexistent/pnpm-lock.yaml", packages);

        // Assert - should not throw and should log warning
        packages.Should().BeEmpty();
        loggingProvider.Logs.Should().Contain(log => log.Message.Contains("not found"));
    }

    [TestMethod]
    public async Task Fetches_license_from_npm_registry_for_pnpm_packages()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var testProject = ChainablePath.Current / "TestCases" / "PnpmApp";
        var pnpmLockPath = (testProject / "pnpm-lock.yaml").ToString();

        var loader = new PnpmLockFileParser(loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata(pnpmLockPath, packages);

        // Assert
        packages.Should().NotBeEmpty();

        // License should be fetched from NPM registry
        var expressPackage = packages.FirstOrDefault(p => p.Name == "express");
        expressPackage.Should().NotBeNull();
        expressPackage!.License.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task Collects_requires_build_and_integrity_from_the_pnpm_lock_file()
    {
        // Arrange
        string lockFilePath = Path.GetTempFileName();
        File.WriteAllText(lockFilePath,
            """
            lockfileVersion: '9.0'
            packages:
              pnpm-with-scripts@1.2.3:
                resolution:
                  integrity: sha512-locked-artifact
                requiresBuild: true
            """);

        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json(
            """{"versions":{"1.2.3":{"scripts":{"install":"node build.js"}}}}"""));
        var metadataFetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance, new HttpClient(handler));
        var parser = new PnpmLockFileParser(NullLogger.Instance, metadataFetcher);
        var packages = new PackageInfoCollection(NullLogger.Instance);

        try
        {
            // Act
            await parser.CollectPackageMetadata(lockFilePath, packages);

            // Assert
            PackageInfo package = packages.Should().ContainSingle().Subject;
            package.Name.Should().Be("pnpm-with-scripts");
            package.HasInstallScripts.Should().BeTrue();
            package.PackageIntegrity.Should().Be("sha512-locked-artifact");
            package.InstallScripts.Should().ContainSingle()
                .Which.Should().Be(new NpmInstallScript("install", "node build.js"));
        }
        finally
        {
            File.Delete(lockFilePath);
        }
    }
}
