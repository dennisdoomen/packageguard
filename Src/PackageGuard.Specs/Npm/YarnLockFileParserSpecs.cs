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
public class YarnLockFileParserSpecs
{
    [TestMethod]
    public async Task Can_collect_package_metadata_from_yarn_lock_file()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var testProject = ChainablePath.Current / "TestCases" / "Yarn1App";
        var yarnLockPath = (testProject / "yarn.lock").ToString();
        var projectPath = testProject.ToString();

        var loader = new YarnLockFileParser(loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata(yarnLockPath, packages);

        // Assert
        packages.Should().NotBeEmpty();

        var expressPackage = packages.FirstOrDefault(p => p.Name == "express");
        expressPackage.Should().NotBeNull();
        expressPackage!.Version.Should().Be("4.18.2");
        expressPackage.Source.Should().Be("npm");
        expressPackage.Projects.Should().Contain(projectPath);

        var lodashPackage = packages.FirstOrDefault(p => p.Name == "lodash");
        lodashPackage.Should().NotBeNull();
        lodashPackage!.Version.Should().Be("4.17.21");

        // Should include scoped package
        var babelPackage = packages.FirstOrDefault(p => p.Name == "@babel/core");
        babelPackage.Should().NotBeNull();
        babelPackage!.Version.Should().Be("7.23.0");
    }

    [TestMethod]
    public async Task Handles_nonexistent_yarn_lock_file_gracefully()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var loader = new YarnLockFileParser(loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata("/nonexistent/yarn.lock", packages);

        // Assert - should not throw and should log warning
        packages.Should().BeEmpty();
        loggingProvider.Logs.Should().Contain(log => log.Message.Contains("not found"));
    }

    [TestMethod]
    public async Task Fetches_license_from_npm_registry_for_yarn_packages()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var testProject = ChainablePath.Current / "TestCases" / "Yarn2App";
        var yarnLockPath = (testProject / "yarn-v2.lock").ToString();

        var loader = new YarnLockFileParser(loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata(yarnLockPath, packages);

        // Assert
        packages.Should().NotBeEmpty();

        // License should be fetched from NPM registry
        var expressPackage = packages.FirstOrDefault(p => p.Name == "express");
        expressPackage.Should().NotBeNull();
        expressPackage!.License.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task Can_collect_package_metadata_from_yarn_v2_lock_file()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();
        var testProject = ChainablePath.Current / "TestCases" / "Yarn2App";
        var yarnV2LockPath = (testProject / "yarn-v2.lock").ToString();
        var projectPath = testProject.ToString();

        var loader = new YarnLockFileParser(loggingProvider.CreateLogger(""));

        var packages = new PackageInfoCollection(loggingProvider.CreateLogger(""));

        // Act
        await loader.CollectPackageMetadata(yarnV2LockPath, packages);

        // Assert
        packages.Should().NotBeEmpty();

        var expressPackage = packages.FirstOrDefault(p => p.Name == "express");
        expressPackage.Should().NotBeNull();
        expressPackage!.Version.Should().Be("4.18.2");
        expressPackage.Source.Should().Be("npm");
        expressPackage.Projects.Should().Contain(projectPath);

        var lodashPackage = packages.FirstOrDefault(p => p.Name == "lodash");
        lodashPackage.Should().NotBeNull();
        lodashPackage!.Version.Should().Be("4.17.21");

        // Should include scoped package
        var babelPackage = packages.FirstOrDefault(p => p.Name == "@babel/core");
        babelPackage.Should().NotBeNull();
        babelPackage!.Version.Should().Be("7.23.0");

        // License should be fetched from NPM registry
        expressPackage.License.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task Collects_install_scripts_and_artifact_integrity_from_yarn_v1()
    {
        // Arrange
        string lockFilePath = Path.GetTempFileName();
        File.WriteAllText(lockFilePath,
            """
            # yarn lockfile v1
            yarn-with-scripts@1.2.3:
              version "1.2.3"
              resolved "https://registry.npmjs.org/yarn-with-scripts/-/yarn-with-scripts-1.2.3.tgz"
              integrity sha512-locked-artifact
            """);

        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json(
            """{"versions":{"1.2.3":{"scripts":{"preinstall":"node prepare.js"}}}}"""));
        var metadataFetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance, new HttpClient(handler));
        var parser = new YarnLockFileParser(NullLogger.Instance, metadataFetcher);
        var packages = new PackageInfoCollection(NullLogger.Instance);

        try
        {
            // Act
            await parser.CollectPackageMetadata(lockFilePath, packages);

            // Assert
            PackageInfo package = packages.Should().ContainSingle().Subject;
            package.HasInstallScripts.Should().BeTrue();
            package.PackageIntegrity.Should().Be("sha512-locked-artifact");
            package.InstallScripts.Should().ContainSingle()
                .Which.Should().Be(new NpmInstallScript("preinstall", "node prepare.js"));
        }
        finally
        {
            File.Delete(lockFilePath);
        }
    }
}
