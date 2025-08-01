using System.IO;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using Pathy;

namespace PackageGuard.Specs;

[TestClass]
public class ConfigurationLoaderSpecs
{
    private string tempDir;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
    }

    [TestCleanup] 
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }
    [TestMethod]
    public void Can_parse_the_configuration_file()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        File.WriteAllText(ChainablePath.Current / "test.json",
            """
            {
                "settings": {
                    "allow": {
                        "prerelease": true,
                        "packages": [
                            "PackageGuard/1.2.3"
                        ],
                        "licenses": [
                            "MIT"
                        ],
                        "feeds": [
                            "https://api.nuget.org/v3/index.json"
                        ]
                    },
                    "deny": {
                        "prerelease": true,
                        "packages": [
                            "Bogus/Package"
                        ],
                        "licenses": [
                            "Proprietary"
                        ]
                    },
                    "ignoredFeeds": [
                        "https://api.nuget.org/v3/index.json"
                    ]
                }
            }
            """);

        // Act
        ConfigurationLoader.Configure(analyzer, "test.json");

        // Assert
        analyzer.Should().BeEquivalentTo(new
        {
            AllowList = new
            {
                Packages = new[]
                {
                    new PackageSelector("PackageGuard", "1.2.3")
                },
                Licenses = new[] { "MIT" },
                Feeds = new[] { "https://api.nuget.org/v3/index.json" },
                Prerelease = true
            },
            DenyList = new
            {
                Packages = new[]
                {
                    new PackageSelector("Bogus", "Package")
                },
                Licenses = new[] { "Proprietary" },
                Prerelease = true
            },
            IgnoredFeeds = new[]
            {
                "https://api.nuget.org/v3/index.json"
            }
        });
    }

    [TestMethod]
    public void Allows_prerelease_packages_by_default()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        File.WriteAllText(ChainablePath.Current / "test.json",
            """
            {
                "settings": {
                    "allow": {
                        "packages": [
                            "PackageGuard/1.2.3"
                        ],
                        "licenses": [
                            "MIT"
                        ],
                    },
                    "deny": {
                    }
                }
            }
            """);

        // Act
        ConfigurationLoader.Configure(analyzer, "test.json");

        // Assert
        analyzer.Should().BeEquivalentTo(new
        {
            AllowList = new
            {
                Prerelease = true
            },
            DenyList = new
            {
                Prerelease = false
            }
        });
    }

    [TestMethod]
    public void Should_find_packageguard_config_in_solution_directory()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        // Create solution directory with solution file and config
        var solutionDir = Path.Combine(tempDir, "MySolution");
        Directory.CreateDirectory(solutionDir);
        File.WriteAllText(Path.Combine(solutionDir, "MySolution.sln"), "# Solution file");
        File.WriteAllText(Path.Combine(solutionDir, "packageguard.config.json"),
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["MIT"],
                        "packages": ["SolutionPackage/1.0.0"]
                    }
                }
            }
            """);

        // Act
        ConfigurationLoader.ConfigureHierarchical(analyzer, solutionDir);

        // Assert
        analyzer.AllowList.Licenses.Should().Contain("MIT");
        analyzer.AllowList.Packages.Should().ContainSingle(p => p.Id == "SolutionPackage");
    }

    [TestMethod]
    public void Should_find_config_in_packageguard_subdirectory()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        // Create solution directory with solution file and config in .packageguard subdirectory
        var solutionDir = Path.Combine(tempDir, "MySolution");
        var packageGuardDir = Path.Combine(solutionDir, ".packageguard");
        Directory.CreateDirectory(packageGuardDir);
        File.WriteAllText(Path.Combine(solutionDir, "MySolution.sln"), "# Solution file");
        File.WriteAllText(Path.Combine(packageGuardDir, "config.json"),
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["Apache-2.0"],
                        "packages": ["SubdirPackage/2.0.0"]
                    }
                }
            }
            """);

        // Act
        ConfigurationLoader.ConfigureHierarchical(analyzer, solutionDir);

        // Assert
        analyzer.AllowList.Licenses.Should().Contain("Apache-2.0");
        analyzer.AllowList.Packages.Should().ContainSingle(p => p.Id == "SubdirPackage");
    }

    [TestMethod]
    public void Should_merge_solution_and_project_configs()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        // Create solution directory with config
        var solutionDir = Path.Combine(tempDir, "MySolution");
        Directory.CreateDirectory(solutionDir);
        File.WriteAllText(Path.Combine(solutionDir, "MySolution.sln"), "# Solution file");
        File.WriteAllText(Path.Combine(solutionDir, "packageguard.config.json"),
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["MIT"],
                        "packages": ["SolutionPackage/1.0.0"]
                    }
                }
            }
            """);

        // Create project directory with additional config
        var projectDir = Path.Combine(solutionDir, "MyProject");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "packageguard.config.json"),
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["Apache-2.0"],
                        "packages": ["ProjectPackage/2.0.0"]
                    }
                }
            }
            """);

        // Act
        ConfigurationLoader.ConfigureHierarchical(analyzer, projectDir);

        // Assert
        analyzer.AllowList.Licenses.Should().Contain(new[] { "MIT", "Apache-2.0" });
        analyzer.AllowList.Packages.Should().HaveCount(2);
        analyzer.AllowList.Packages.Should().ContainSingle(p => p.Id == "SolutionPackage");
        analyzer.AllowList.Packages.Should().ContainSingle(p => p.Id == "ProjectPackage");
    }

    [TestMethod]
    public void Should_allow_project_config_to_override_solution_prerelease_setting()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        // Create solution directory with prerelease allowed
        var solutionDir = Path.Combine(tempDir, "MySolution");
        Directory.CreateDirectory(solutionDir);
        File.WriteAllText(Path.Combine(solutionDir, "MySolution.sln"), "# Solution file");
        File.WriteAllText(Path.Combine(solutionDir, "packageguard.config.json"),
            """
            {
                "settings": {
                    "allow": {
                        "prerelease": true
                    }
                }
            }
            """);

        // Create project directory that disallows prerelease
        var projectDir = Path.Combine(solutionDir, "MyProject");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "packageguard.config.json"),
            """
            {
                "settings": {
                    "allow": {
                        "prerelease": false
                    }
                }
            }
            """);

        // Act
        ConfigurationLoader.ConfigureHierarchical(analyzer, projectDir);

        // Assert
        analyzer.AllowList.Prerelease.Should().BeFalse("project-level setting should override solution-level");
    }

    [TestMethod]
    public void Should_do_nothing_when_no_configs_found()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        // Create directory without solution or config files
        var emptyDir = Path.Combine(tempDir, "EmptyDir");
        Directory.CreateDirectory(emptyDir);

        // Act
        ConfigurationLoader.ConfigureHierarchical(analyzer, emptyDir);

        // Assert
        analyzer.AllowList.Licenses.Should().BeEmpty();
        analyzer.AllowList.Packages.Should().BeEmpty();
        analyzer.AllowList.Prerelease.Should().BeTrue("default value");
    }

    [TestMethod]
    public void Should_find_solution_in_parent_directory()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        // Create solution in parent directory
        var solutionDir = Path.Combine(tempDir, "MySolution");
        Directory.CreateDirectory(solutionDir);
        File.WriteAllText(Path.Combine(solutionDir, "MySolution.sln"), "# Solution file");
        File.WriteAllText(Path.Combine(solutionDir, "packageguard.config.json"),
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["BSD-3-Clause"]
                    }
                }
            }
            """);

        // Create nested project directory
        var projectDir = Path.Combine(solutionDir, "src", "MyProject");
        Directory.CreateDirectory(projectDir);

        // Act
        ConfigurationLoader.ConfigureHierarchical(analyzer, projectDir);

        // Assert
        analyzer.AllowList.Licenses.Should().Contain("BSD-3-Clause");
    }

    [TestMethod]
    public void Should_not_include_sibling_project_configs()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        // Create solution directory with config
        var solutionDir = Path.Combine(tempDir, "MySolution");
        Directory.CreateDirectory(solutionDir);
        File.WriteAllText(Path.Combine(solutionDir, "MySolution.sln"), "# Solution file");
        File.WriteAllText(Path.Combine(solutionDir, "packageguard.config.json"),
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["MIT"]
                    }
                }
            }
            """);

        // Create ProjectA with its own config
        var projectADir = Path.Combine(solutionDir, "ProjectA");
        Directory.CreateDirectory(projectADir);
        File.WriteAllText(Path.Combine(projectADir, "packageguard.config.json"),
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["Apache-2.0"],
                        "packages": ["ProjectAPackage/1.0.0"]
                    }
                }
            }
            """);

        // Create ProjectB with its own config
        var projectBDir = Path.Combine(solutionDir, "ProjectB");
        Directory.CreateDirectory(projectBDir);
        File.WriteAllText(Path.Combine(projectBDir, "packageguard.config.json"),
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["BSD-3-Clause"],
                        "packages": ["ProjectBPackage/2.0.0"]
                    }
                }
            }
            """);

        // Act - Configure for ProjectA only
        ConfigurationLoader.ConfigureHierarchical(analyzer, projectADir);

        // Assert - Should have solution config + ProjectA config, but NOT ProjectB config
        analyzer.AllowList.Licenses.Should().Contain(new[] { "MIT", "Apache-2.0" });
        analyzer.AllowList.Licenses.Should().NotContain("BSD-3-Clause");
        analyzer.AllowList.Packages.Should().HaveCount(1);
        analyzer.AllowList.Packages.Should().ContainSingle(p => p.Id == "ProjectAPackage");
        analyzer.AllowList.Packages.Should().NotContain(p => p.Id == "ProjectBPackage");
    }
}
