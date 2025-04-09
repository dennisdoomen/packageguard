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
    public async Task Either_a_denylist_or_a_allowlist_is_required()
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
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Either*allowlist*denylist*");
    }


    [TestMethod]
    public async Task Can_denylist_an_entire_package()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                DenyList = new()
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
    public async Task Can_denylist_a_specific_version()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                DenyList = new()
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
    public async Task Does_not_denylist_a_version_if_the_range_does_not_match()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                DenyList = new()
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
    public async Task Can_denylist_a_version_based_on_a_range()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                DenyList = new()
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
    public async Task Can_denyist_a_license()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                DenyList = new()
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
    public async Task Can_allowlist_a_license()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                AllowList = new()
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
    public async Task Can_allowlist_an_unknown_license()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                AllowList = new()
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
    public async Task Denylisting_a_license_overrides_a_allowlisted_license()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                AllowList =
                {
                    Licenses = ["mit"]
                },
                DenyList = new()
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
    public async Task A_package_version_outside_the_allowlisted_range_is_a_violation()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                AllowList = new()
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
    public async Task A_package_version_inside_the_allowlisted_range_is_okay()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                AllowList = new()
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
    public async Task Can_allowlist_a_package_that_violates_the_allowlisted_licenses()
    {
        // Arrange
        var analyzer =
            new NuGetProjectAnalyzer(new ProjectScanner(NullLogger.Instance), new NuGetPackageAnalyzer(NullLogger.Instance))
            {
                ProjectPath = ProjectPath,
                AllowList = new()
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
