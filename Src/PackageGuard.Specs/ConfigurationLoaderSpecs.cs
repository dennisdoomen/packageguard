using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Meziantou.Extensions.Logging.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Policy;
using Pathy;

namespace PackageGuard.Specs;

[TestClass]
public class ConfigurationLoaderSpecs
{
    private ChainablePath tempDir;
    private ConfigurationLoader configurationLoader;

    [TestInitialize]
    public void Setup()
    {
        tempDir = ChainablePath.Temp / Path.GetRandomFileName();
        Directory.CreateDirectory(tempDir);

        configurationLoader = new ConfigurationLoader(NullLogger.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        tempDir.DeleteFileOrDirectory();
    }

    [TestMethod]
    public void Can_parse_the_configuration_file()
    {
        // Arrange
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
        ProjectPolicy policy = configurationLoader.GetConfigurationFromConfigPath("test.json");

        // Assert
        policy.Should().BeEquivalentTo(new
        {
            AllowList = new
            {
                Packages = new[]
                {
                    new PackageSelector("PackageGuard", "1.2.3") { SourceFile = "test.json" }
                },
                Licenses = new[]
                {
                    "MIT"
                },
                Feeds = new[]
                {
                    "https://api.nuget.org/v3/index.json"
                },
                Prerelease = true
            },
            DenyList = new
            {
                Packages = new[]
                {
                    new PackageSelector("Bogus", "Package") { SourceFile = "test.json" }
                },
                Licenses = new[]
                {
                    "Proprietary"
                },
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
        ProjectPolicy policy = configurationLoader.GetConfigurationFromConfigPath("test.json");

        // Assert
        policy.Should().BeEquivalentTo(new
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
    public void Can_find_the_config_in_the_solution_directory()
    {
        // Arrange

        // Create solution directory with solution file and config
        var solutionDir = tempDir / "MySolution";
        solutionDir.CreateDirectoryRecursively();

        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(solutionDir / "packageguard.config.json",
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
        ProjectPolicy policy = configurationLoader.GetEffectiveConfigurationForProject(solutionDir);

        // Assert
        policy.AllowList.Licenses.Should().Contain("MIT");
        policy.AllowList.Packages.Should().ContainSingle(p => p.Id == "SolutionPackage");
    }

    [TestMethod]
    public void Can_find_the_config_in_a_subdirectory()
    {
        // Arrange

        // Create solution directory with solution file and config in .packageguard subdirectory
        var solutionDir = tempDir / "MySolution";
        var packageGuardDir = solutionDir / ".packageguard";
        packageGuardDir.CreateDirectoryRecursively();

        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(packageGuardDir / "config.json",
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
        ProjectPolicy policy = configurationLoader.GetEffectiveConfigurationForProject(solutionDir);

        // Assert
        policy.AllowList.Licenses.Should().Contain("Apache-2.0");
        policy.AllowList.Packages.Should().ContainSingle(p => p.Id == "SubdirPackage");
    }

    [TestMethod]
    public void Will_merge_the_solution_and_project_configs()
    {
        // Arrange

        // Create solution directory with config
        var solutionDir = tempDir / "MySolution";
        solutionDir.CreateDirectoryRecursively();

        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(solutionDir / "packageguard.config.json",
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
        var projectDir = solutionDir / "MyProject";
        projectDir.CreateDirectoryRecursively();

        File.WriteAllText(projectDir / "packageguard.config.json",
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
        ProjectPolicy policy = configurationLoader.GetEffectiveConfigurationForProject(projectDir);

        // Assert
        policy.AllowList.Licenses.Should().Contain([
            "MIT",
            "Apache-2.0"
        ]);

        policy.AllowList.Packages.Should().HaveCount(2);
        policy.AllowList.Packages.Should().ContainSingle(p => p.Id == "SolutionPackage");
        policy.AllowList.Packages.Should().ContainSingle(p => p.Id == "ProjectPackage");
    }

    [TestMethod]
    public void Project_settings_override_solution_settings()
    {
        // Arrange

        // Create solution directory with prerelease allowed
        var solutionDir = tempDir / "MySolution";
        Directory.CreateDirectory(solutionDir);
        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(solutionDir / "packageguard.config.json",
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
        var projectDir = solutionDir / "MyProject";
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(projectDir / "packageguard.config.json",
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
        var policy = configurationLoader.GetEffectiveConfigurationForProject(projectDir);

        // Assert
        policy.AllowList.Prerelease.Should().BeFalse("project-level setting should override solution-level");
    }

    [TestMethod]
    public void Does_not_do_anything_if_no_config_is_found()
    {
        // Arrange
        // Create directory without solution or config files
        var emptyDir = tempDir / "EmptyDir";
        emptyDir.CreateDirectoryRecursively();

        // Act
        ProjectPolicy policy = configurationLoader.GetEffectiveConfigurationForProject(emptyDir);

        // Assert
        policy.AllowList.Licenses.Should().BeEmpty();
        policy.AllowList.Packages.Should().BeEmpty();
        policy.AllowList.Prerelease.Should().BeTrue("default value");
    }

    [TestMethod]
    public void Finds_the_solution_config_in_the_parent_directory_of_the_project()
    {
        // Arrange
        // Create solution in parent directory
        var solutionDir = tempDir / "MySolution";
        solutionDir.CreateDirectoryRecursively();

        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(solutionDir / "packageguard.config.json",
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
        var projectDir = solutionDir / "src" / "MyProject";
        projectDir.CreateDirectoryRecursively();

        // Act
        ProjectPolicy policy = configurationLoader.GetEffectiveConfigurationForProject(projectDir);

        // Assert
        policy.AllowList.Licenses.Should().Contain("BSD-3-Clause");
    }

    [TestMethod]
    public void Ignores_the_settings_of_sibling_folders()
    {
        // Arrange
        // Create solution directory with config
        var solutionDir = tempDir / "MySolution";
        solutionDir.CreateDirectoryRecursively();

        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(solutionDir / "packageguard.config.json",
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
        var projectADir = solutionDir / "ProjectA";
        projectADir.CreateDirectoryRecursively();

        File.WriteAllText(projectADir / "packageguard.config.json",
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
        var projectBDir = solutionDir / "ProjectB";
        projectBDir.CreateDirectoryRecursively();

        File.WriteAllText(projectBDir / "packageguard.config.json",
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
        ProjectPolicy policy = configurationLoader.GetEffectiveConfigurationForProject(projectADir);

        // Assert - Should have solution config + ProjectA config, but NOT ProjectB config
        policy.AllowList.Licenses.Should().Contain([
            "MIT",
            "Apache-2.0"
        ]);

        policy.AllowList.Licenses.Should().NotContain("BSD-3-Clause");
        policy.AllowList.Packages.Should().HaveCount(1);
        policy.AllowList.Packages.Should().ContainSingle(p => p.Id == "ProjectAPackage");
        policy.AllowList.Packages.Should().NotContain(p => p.Id == "ProjectBPackage");
    }

    [TestMethod]
    public void Tracks_which_config_file_each_rule_came_from()
    {
        // Arrange
        var solutionDir = tempDir / "MySolution";
        solutionDir.CreateDirectoryRecursively();

        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(solutionDir / "packageguard.config.json",
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

        var projectDir = solutionDir / "MyProject";
        projectDir.CreateDirectoryRecursively();

        File.WriteAllText(projectDir / "packageguard.config.json",
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
        ProjectPolicy policy = configurationLoader.GetEffectiveConfigurationForProject(projectDir);

        // Assert
        string solutionConfigPath = solutionDir / "packageguard.config.json";
        string projectConfigPath = projectDir / "packageguard.config.json";

        policy.AllowList.Packages.Should().ContainSingle(p => p.Id == "SolutionPackage")
            .Which.SourceFile.Should().Be(solutionConfigPath);
        policy.AllowList.Packages.Should().ContainSingle(p => p.Id == "ProjectPackage")
            .Which.SourceFile.Should().Be(projectConfigPath);

        policy.AllowList.LicenseSourceFiles["MIT"].Should().Be(solutionConfigPath);
        policy.AllowList.LicenseSourceFiles["Apache-2.0"].Should().Be(projectConfigPath);
    }

    [TestMethod]
    public void Applies_different_effective_configurations_to_different_projects()
    {
        // Test that demonstrates each project gets its own merged configuration
        // when using hierarchical configuration

        // Arrange - Create solution with different project configurations
        var solutionDir = tempDir / "MySolution";
        solutionDir.CreateDirectoryRecursively();

        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");

        // Solution-level config allows MIT
        File.WriteAllText(solutionDir / "packageguard.config.json",
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["MIT"]
                    }
                }
            }
            """);

        // ProjectA additionally allows Apache-2.0
        var projectADir = solutionDir / "ProjectA";
        projectADir.CreateDirectoryRecursively();

        File.WriteAllText(projectADir / "packageguard.config.json",
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["Apache-2.0"]
                    }
                }
            }
            """);

        // ProjectB additionally allows BSD-3-Clause
        var projectBDir = solutionDir / "ProjectB";
        projectBDir.CreateDirectoryRecursively();

        File.WriteAllText(projectBDir / "packageguard.config.json",
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["BSD-3-Clause"]
                    }
                }
            }
            """);

        // Act & Assert - Test ProjectA configuration
        var projectAConfig = configurationLoader.GetEffectiveConfigurationForProject(projectADir);
        projectAConfig.AllowList.Licenses.Should().Contain([
            "MIT",
            "Apache-2.0"
        ]);

        projectAConfig.AllowList.Licenses.Should().NotContain("BSD-3-Clause");

        // Act & Assert - Test ProjectB configuration
        var projectBConfig = configurationLoader.GetEffectiveConfigurationForProject(projectBDir);
        projectBConfig.AllowList.Licenses.Should().Contain([
            "MIT",
            "BSD-3-Clause"
        ]);

        projectBConfig.AllowList.Licenses.Should().NotContain("Apache-2.0");
    }

    [TestMethod]
    public void Can_parse_risk_gating_settings()
    {
        // Arrange
        File.WriteAllText(ChainablePath.Current / "test.json",
            """
            {
                "settings": {
                    "deny": {
                        "maxOverallRisk": 60,
                        "maxLegalRisk": 5,
                        "maxSecurityRisk": 7,
                        "maxOperationalRisk": 8,
                        "maxOsvSeverityScore": 7.0,
                        "denyUnsigned": true,
                        "denyDeprecated": true,
                        "denyWithoutRepository": true,
                        "minPackageAgeDays": {
                            "npm": 14,
                            "nuget": 3
                        }
                    },
                    "warn": {
                        "licenses": ["GPL-3.0"],
                        "packages": ["some-package"]
                    },
                    "riskExceptions": [
                        {
                            "package": "left-pad",
                            "reason": "Vetted manually",
                            "expiresOn": "2026-07-01"
                        },
                        {
                            "package": "some-native-lib",
                            "versions": "[1.0.0,2.0.0)",
                            "reason": "Signing certificate expired but publisher identity verified out-of-band"
                        }
                    ]
                }
            }
            """);

        // Act
        ProjectPolicy policy = configurationLoader.GetConfigurationFromConfigPath("test.json");

        // Assert
        policy.DenyList.MaxOverallRisk.Should().Be(60);
        policy.DenyList.MaxLegalRisk.Should().Be(5);
        policy.DenyList.MaxSecurityRisk.Should().Be(7);
        policy.DenyList.MaxOperationalRisk.Should().Be(8);
        policy.DenyList.MaxOsvSeverityScore.Should().Be(7.0);
        policy.DenyList.DenyUnsigned.Should().BeTrue();
        policy.DenyList.DenyDeprecated.Should().BeTrue();
        policy.DenyList.DenyWithoutRepository.Should().BeTrue();
        policy.DenyList.MinPackageAgeDays.Should().BeEquivalentTo(new Dictionary<string, int> { ["npm"] = 14, ["nuget"] = 3 });

        policy.WarnList.Licenses.Should().Contain("GPL-3.0");
        policy.WarnList.Packages.Should().ContainSingle(p => p.Id == "some-package");

        policy.RiskExceptions.Should().HaveCount(2);
        policy.RiskExceptions.Should().ContainSingle(exception => exception.Package == "left-pad")
            .Which.ExpiresOn.Should().Be(new DateOnly(2026, 7, 1));
        policy.RiskExceptions.Should().ContainSingle(exception => exception.Package == "some-native-lib")
            .Which.Versions.Should().Be("[1.0.0,2.0.0)");
    }

    [TestMethod]
    public void Project_level_config_without_a_risk_threshold_keeps_the_solution_level_threshold()
    {
        // Arrange
        var solutionDir = tempDir / "MySolution";
        solutionDir.CreateDirectoryRecursively();
        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(solutionDir / "packageguard.config.json",
            """
            {
                "settings": {
                    "deny": {
                        "maxOverallRisk": 60
                    }
                }
            }
            """);

        var projectDir = solutionDir / "MyProject";
        projectDir.CreateDirectoryRecursively();
        File.WriteAllText(projectDir / "packageguard.config.json",
            """
            {
                "settings": {
                    "deny": {
                        "packages": ["Bogus/1.0.0"]
                    }
                }
            }
            """);

        // Act
        ProjectPolicy policy = configurationLoader.GetEffectiveConfigurationForProject(projectDir);

        // Assert
        policy.DenyList.MaxOverallRisk.Should().Be(60, "the project config didn't set this, so the solution-level threshold should still apply");
    }

    [TestMethod]
    public void Project_level_config_can_override_the_solution_level_risk_threshold()
    {
        // Arrange
        var solutionDir = tempDir / "MySolution";
        solutionDir.CreateDirectoryRecursively();
        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(solutionDir / "packageguard.config.json",
            """
            {
                "settings": {
                    "deny": {
                        "maxOverallRisk": 60
                    }
                }
            }
            """);

        var projectDir = solutionDir / "MyProject";
        projectDir.CreateDirectoryRecursively();
        File.WriteAllText(projectDir / "packageguard.config.json",
            """
            {
                "settings": {
                    "deny": {
                        "maxOverallRisk": 40
                    }
                }
            }
            """);

        // Act
        ProjectPolicy policy = configurationLoader.GetEffectiveConfigurationForProject(projectDir);

        // Assert
        policy.DenyList.MaxOverallRisk.Should().Be(40);
    }

    [TestMethod]
    public void Falls_back_to_the_scan_root_when_a_project_lives_outside_the_solution_directory()
    {
        // Arrange
        // The solution lives in "src", but references a sibling "build" project, so the solution
        // directory is not an ancestor of that project's own directory.
        var srcDir = tempDir / "src";
        srcDir.CreateDirectoryRecursively();

        File.WriteAllText(srcDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(srcDir / "packageguard.config.json",
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["MIT"]
                    }
                }
            }
            """);

        var buildDir = tempDir / "build";
        buildDir.CreateDirectoryRecursively();

        var loader = new ConfigurationLoader(NullLogger.Instance, srcDir);

        // Act
        ProjectPolicy policy = loader.GetEffectiveConfigurationForProject(buildDir);

        // Assert
        policy.AllowList.Licenses.Should().Contain("MIT");
    }

    [TestMethod]
    public void Falls_back_to_the_current_directory_when_no_path_argument_was_given()
    {
        // Arrange
        // Mirrors the CLI default: no [path] argument means ProjectPath is "" (not null), and the
        // solution is found via the current directory rather than an explicit path.
        var srcDir = tempDir / "src";
        srcDir.CreateDirectoryRecursively();

        File.WriteAllText(srcDir / "MySolution.sln", "# Solution file");
        File.WriteAllText(srcDir / "packageguard.config.json",
            """
            {
                "settings": {
                    "allow": {
                        "licenses": ["MIT"]
                    }
                }
            }
            """);

        var buildDir = tempDir / "build";
        buildDir.CreateDirectoryRecursively();

        var loader = new ConfigurationLoader(NullLogger.Instance, string.Empty);

        string originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(srcDir);

            // Act
            ProjectPolicy policy = loader.GetEffectiveConfigurationForProject(buildDir);

            // Assert
            policy.AllowList.Licenses.Should().Contain("MIT");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [TestMethod]
    public void Logs_the_directories_it_searched_for_configuration_files()
    {
        // Arrange
        var solutionDir = tempDir / "MySolution";
        solutionDir.CreateDirectoryRecursively();

        File.WriteAllText(solutionDir / "MySolution.sln", "# Solution file");

        var loggingProvider = new InMemoryLoggerProvider();
        var loader = new ConfigurationLoader(loggingProvider.CreateLogger(""));

        // Act
        loader.GetEffectiveConfigurationForProject(solutionDir);

        // Assert
        loggingProvider.Logs.Select(x => x.Message)
            .Should().ContainMatch($"*Looking for configuration files in {solutionDir}*");
    }
}
