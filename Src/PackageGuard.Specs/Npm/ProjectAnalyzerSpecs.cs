using System;
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
    public async Task Can_analyze_npm_projects()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "NpmApp";

        // Act
        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["not-mit"]
            },
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            FeedName = "npm",
            FeedUrl = "https://registry.npmjs.org/cookie/-/cookie-0.5.0.tgz",
            License = "MIT",
            PackageId = "cookie",
            Version = "0.5.0"
        });
    }

    [TestMethod]
    public async Task Can_analyze_npm_projects_without_lock_file()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "NpmAppWithoutLockFile";
        project.GlobFiles("*-lock.json", "node_modules\\**").DeleteFileOrDirectory();

        // Act
        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "apache-2.0"]
            },
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            FeedName = "npm",
            FeedUrl = "https://registry.npmjs.org/inherits/-/inherits-2.0.4.tgz",
            License = "ISC",
            PackageId = "inherits",
            Version = "2.0.4"
        });
    }

    [TestMethod]
    public async Task Can_fetch_the_npm_license_information_if_the_lock_file_did_not_contain_it()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "NpmAppNoLicense";

        // Act
        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings
        {
            SkipRestore = true
        }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["not-mit"]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            FeedName = "npm",
            FeedUrl = "https://registry.npmjs.org/is-number/-/is-number-7.0.0.tgz",
            License = "MIT",
            PackageId = "is-number",
            Version = "7.0.0"
        });
    }

    [TestMethod]
    public async Task Can_analyze_yarn_projects()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "YarnApp";
        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings
        {
            NpmPackageManager = NpmPackageManager.Yarn,
        }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["not-mit"]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            FeedName = "npm",
            FeedUrl = "https://registry.yarnpkg.com/debug/-/debug-2.6.9.tgz#5d128515df134ff327e90a4c93f4e077a536341f",
            License = "MIT",
            PackageId = "debug",
            Version = "2.6.9"
        });
    }

    [TestMethod]
    public async Task Can_analyze_yarn_projects_without_lock_file()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "YarnAppWithoutLockFile";
        project.GlobFiles("*lock.json", "node_modules\\**").DeleteFileOrDirectory();

        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings
        {
            NpmPackageManager = NpmPackageManager.Yarn,
        }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["not-mit"]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            FeedName = "npm",
            FeedUrl = "https://registry.yarnpkg.com/debug/-/debug-2.6.9.tgz#5d128515df134ff327e90a4c93f4e077a536341f",
            License = "MIT",
            PackageId = "debug",
            Version = "2.6.9"
        });
    }

    [TestMethod]
    public async Task Can_analyze_pnpm_projects()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "PNpmApp";
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
                Licenses = ["not-mit"]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            FeedName = "npm",
            FeedUrl = "https://registry.npmjs.org/content-type/-/content-type-1.0.5.tgz",
            License = "MIT",
            PackageId = "content-type",
            Version = "1.0.5"
        });
    }

    [TestMethod]
    public async Task Can_analyze_pnpm_projects_without_lock_file()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "PnpmAppWithoutLockFile";
        project.GlobFiles("*lock.json", "node_modules\\**").DeleteFileOrDirectory();

        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        var violations = await analyzer.ExecuteAnalysis(project, new AnalyzerSettings
        {
            NpmPackageManager = NpmPackageManager.Pnpm,
        }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["not-mit"]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            FeedName = "npm",
            FeedUrl = "https://registry.npmjs.org/content-type/-/content-type-1.0.5.tgz",
            License = "MIT",
            PackageId = "content-type",
            Version = "1.0.5"
        });
    }
}
