using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
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
                    new PackageSelector("PackageGuard", "1.2.3")
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
                    new PackageSelector("Bogus", "Package")
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
}
