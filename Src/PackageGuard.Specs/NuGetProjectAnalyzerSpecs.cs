using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
public class NuGetProjectAnalyzerSpecs
{
    public TestContext TestContext { get; set; }

    private string ProjectPath => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..\\..\\..\\PackageGuard.Specs.csproj");

    [TestMethod]
    public async Task Either_a_blacklist_or_a_whitelist_is_required()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
            };

        // Act
        var act = async () => await analyzer.ExecuteAnalysis();

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Either*whitelist*blacklist*");
    }


    [TestMethod]
    public async Task Can_blacklist_an_entire_package()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                BlackList = new()
                {
                    Packages =
                    [
                        new PackageSelector("FluentAssertions")
                    ]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Should().ContainEquivalentOf(new { PackageId = "FluentAssertions", Version = "8.2.0", License = "Unknown" });
    }

    [TestMethod]
    public async Task Can_blacklist_a_specific_version()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                BlackList = new()
                {
                    Packages =
                    [
                        new PackageSelector("FluentAssertions", "8.2.0")
                    ]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Should().ContainEquivalentOf(new { PackageId = "FluentAssertions", Version = "8.2.0", License = "Unknown" });
    }

    [TestMethod]
    public async Task Does_not_blacklist_a_version_if_the_range_does_not_match()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                BlackList = new()
                {
                    Packages =
                    [
                        new PackageSelector("FluentAssertions", "[7.0.0,8.0.0)"),
                    ]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Can_blacklist_a_version_based_on_a_range()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                BlackList = new()
                {
                    Packages =
                    [
                        new PackageSelector("FluentAssertions", "[8.0.0,9.0.0)"),
                    ]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Should().ContainEquivalentOf(new { PackageId = "FluentAssertions", Version = "8.2.0", License = "Unknown" });
    }

    [TestMethod]
    public async Task Can_blacklist_a_license()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                BlackList = new()
                {
                    Licenses = ["mit"]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Select(x => x.PackageId).Should().Contain(["CliWrap", "coverlet.collector", "JetBrains.Annotations"]);
        violations.Should().NotContainEquivalentOf(new
        {
            PackageId = "FluentAssertions"
        });
    }

    [TestMethod]
    public async Task Can_whitelist_a_license()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                WhiteList = new()
                {
                    Licenses = ["mit"]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "FluentAssertions"
        });
    }

    [TestMethod]
    public async Task Can_whitelist_an_unknown_license()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                WhiteList = new()
                {
                    Licenses = ["mit", "apache-2.0", "unknown"]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Blacklisting_a_license_overrides_a_whitelisted_license()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                WhiteList =
                {
                    Licenses = ["mit"]
                },
                BlackList = new()
                {
                    Licenses = ["mit"]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Select(x => x.PackageId).Should().Contain(["CliWrap", "coverlet.collector", "JetBrains.Annotations"]);
    }

    [TestMethod]
    public async Task A_package_version_outside_the_whitelisted_range_is_a_violation()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                WhiteList = new()
                {
                    Packages =
                    [
                        new PackageSelector("FluentAssertions", "[7.0.0,8.0.0)"),
                    ]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Should().ContainEquivalentOf(new { PackageId = "FluentAssertions", Version = "8.2.0", License = "Unknown" });
    }

    [TestMethod]
    public async Task A_package_version_inside_the_whitelisted_range_is_okay()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                WhiteList = new()
                {
                    Packages =
                    [
                        new PackageSelector("FluentAssertions", "[8.0.0,9.0.0)"),
                    ]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Can_whitelist_a_package_that_violates_the_whitelisted_licenses()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                WhiteList = new()
                {
                    Licenses = ["mit", "apache-2.0"],
                    Packages = [new PackageSelector("FluentAssertions")]
                }
            };

        // Act
        var violations = await analyzer.ExecuteAnalysis();

        // Assert
        violations.Should().BeEmpty();
    }

}
