using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using PackageGuard.Specs.Common;
using Pathy;

namespace PackageGuard.Specs.Npm;

[TestClass]
public class ProjectAnalyzerSpecs
{
    private readonly LicenseFetcher licenseFetcher =
        new(NullLogger.Instance, Environment.GetEnvironmentVariable("GITHUB_API_KEY"));

    [TestMethod]
    public async Task Runs_install_to_get_the_missing_lock_file()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "NpmAppWithoutLockFile";
        project.GlobFiles("*-lock.json", "node_modules\\**").ForEach(f => f.DeleteFileOrDirectory());

        // Act
        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings(), _ => new ProjectPolicy()
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "apache-2.0"]
            }
        });

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Can_analyze_yarn_projects()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "NpmApp";
        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings
        {
            NpmPackageManager = NpmPackageManager.Yarn,
            SkipRestore = true
        }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit"]
            }
        });

        // Assert
        violations.Should().NotBeEmpty();
        violations.Select(v => v.PackageId).Should().Contain("@ampproject/remapping");
    }

    [TestMethod]
    public async Task Can_analyze_pnpm_projects()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "NpmApp";
        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings
        {
            NpmPackageManager = NpmPackageManager.Pnpm,
            SkipRestore = true
        }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit"]
            }
        });

        // Assert
        violations.Should().NotBeEmpty();
        violations.Select(v => v.PackageId).Should().Contain("@ampproject/remapping");
    }

    [TestMethod]
    public async Task Yarn_projects_respect_allowlist()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "NpmApp";
        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings
        {
            NpmPackageManager = NpmPackageManager.Yarn,
            SkipRestore = true
        }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "apache-2.0", "bsd-3-clause", "bsd-2-clause", "isc", "0bsd", "cc-by-4.0", "python-2.0"]
            }
        });

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Pnpm_projects_respect_allowlist()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "NpmApp";
        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings
        {
            NpmPackageManager = NpmPackageManager.Pnpm,
            SkipRestore = true
        }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "apache-2.0", "bsd-3-clause", "bsd-2-clause", "isc", "0bsd", "cc-by-4.0", "python-2.0"]
            }
        });

        // Assert
        violations.Should().BeEmpty();
    }
}
