using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CliWrap;
using FluentAssertions;
using Meziantou.Extensions.Logging.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using PackageGuard.Core.CSharp;
using Pathy;

namespace PackageGuard.Specs;

[TestClass]
public class ProjectAnalyzerSpecs
{
    private readonly CSharpProjectScanner cSharpProjectScanner = new(NullLogger.Instance);

    private readonly LicenseFetcher licenseFetcher =
        new(NullLogger.Instance, Environment.GetEnvironmentVariable("GITHUB_API_KEY"));

    private string ProjectPath =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "..", "..", "..", "PackageGuard.Specs.csproj"));

    [TestMethod]
    public async Task Either_a_denylist_or_a_allowlist_is_required()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var act = async () => await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy());

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Either*allowlist*denylist*");
    }

    [TestMethod]
    public async Task Can_deny_an_entire_package()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            DenyList = new DenyList
            {
                Packages =
                [
                    new PackageSelector("FluentAssertions")
                ]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "FluentAssertions",
            Version = Value.ThatSatisfies<string>(s => s.Should().StartWith("8.")),
            License = "Unknown"
        });
    }

    [TestMethod]
    public async Task Can_deny_a_specific_version()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            DenyList = new DenyList
            {
                Packages =
                [
                    new PackageSelector("FluentAssertions", "8.5.0")
                ]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "FluentAssertions",
            Version = Value.ThatSatisfies<string>(s => s.Should().StartWith("8.")),
            License = "Unknown"
        });
    }

    [TestMethod]
    public async Task The_version_must_a_valid_string()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var act = async () => await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            DenyList = new DenyList
            {
                Packages =
                [
                    new PackageSelector("FluentAssertions", "blah")
                ]
            }
        });

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not a valid version string.*");
    }

    [TestMethod]
    public async Task Does_not_deny_a_version_if_the_range_does_not_match()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            DenyList = new DenyList
            {
                Packages =
                [
                    new PackageSelector("FluentAssertions", "[7.0.0,8.0.0)"),
                ]
            }
        });

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Can_deny_a_version_based_on_a_range()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            DenyList = new DenyList
            {
                Packages =
                [
                    new PackageSelector("FluentAssertions", "[8.0.0,9.0.0)"),
                ]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "FluentAssertions",
            Version = Value.ThatSatisfies<string>(s => s.Should().StartWith("8.")),
            License = "Unknown"
        });
    }

    [TestMethod]
    public async Task Can_denyist_a_license()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            DenyList = new DenyList
            {
                Licenses = ["mit"]
            }
        });

        // Assert
        violations.Select(x => x.PackageId).Should().Contain(["CliWrap", "coverlet.collector", "JetBrains.Annotations"]);
        violations.Should().NotContainEquivalentOf(new
        {
            PackageId = "FluentAssertions"
        });
    }

    [TestMethod]
    public async Task Can_allow_an_entire_package_using_an_empty_version()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Packages =
                [
                    new PackageSelector("FluentAssertions", string.Empty)
                ]
            }
        });

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Can_allow_a_license()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit"]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "FluentAssertions"
        });
    }

    [TestMethod]
    public async Task Can_allow_an_unknown_license()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "apache-2.0", "unknown"]
            }
        });

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Denying_a_license_overrides_an_allowed_license()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit"]
            },
            DenyList = new DenyList
            {
                Licenses = ["mit"]
            }
        });

        // Assert
        violations.Select(x => x.PackageId).Should().Contain(["CliWrap", "coverlet.collector", "JetBrains.Annotations"]);
    }

    [TestMethod]
    public async Task A_version_outside_the_allowed_range_is_a_violation()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Packages =
                [
                    new PackageSelector("FluentAssertions", "[7.0.0,8.0.0)"),
                ]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "FluentAssertions",
            Version = Value.ThatSatisfies<string>(s => s.Should().StartWith("8.")),
            License = "Unknown"
        });
    }

    [TestMethod]
    public async Task A_version_inside_the_allowed_range_is_okay()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Packages =
                [
                    new PackageSelector("FluentAssertions", "[8.0.0,9.0.0)"),
                ]
            }
        });

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Can_still_allow_a_package_that_violates_the_allowed_licenses()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);

        // Act
        var violations = await analyzer.ExecuteAnalysis(ProjectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "apache-2.0"],
                Packages = [new PackageSelector("FluentAssertions")]
            }
        });

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task A_specified_project_must_exist()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = ChainablePath.Current / "NonExistingFolder" / "NonExisting.csproj";

        // Act
        var act = () => analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings(), _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit"]
            }
        });

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*file*NonExisting.csproj*does not exist*");
    }

    [TestMethod]
    public async Task Allowing_a_feed_by_name_even_allows_a_package_which_license_is_not_allowed()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = ChainablePath.Current / "TestCases" / "UnknownLicense" / "ConsoleApp.csproj";

        // Act
        var violations = await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings { ForceRestore = true }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit"],
                Feeds = ["nuget.org"]
            }
        });

        // Assert
        violations.Should().BeEmpty("Since we excluded the feed");
    }

    [TestMethod]
    public async Task Allowing_a_feed_by_url_even_allows_a_package_which_license_is_not_allowed()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = ChainablePath.Current / "TestCases" / "UnknownLicense" / "ConsoleApp.csproj";

        // Act
        var violations = await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings { ForceRestore = true }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit"],
                Feeds = ["*v3/index.json*"]
            }
        });

        // Assert
        violations.Should().BeEmpty("Since we excluded the feed");
    }

    [TestMethod]
    public async Task Can_still_deny_a_package_from_an_allowed_feed()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = ChainablePath.Current / "TestCases" / "UnknownLicense" / "ConsoleApp.csproj";

        // Act
        var violations = await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings { ForceRestore = true }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit"],
                Feeds = ["nuget.org"]
            },
            DenyList = new DenyList
            {
                Packages = [new PackageSelector("FluentAssertions")]
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "FluentAssertions",
            Version = "8.3.0",
            License = "Unknown"
        });
    }

    [TestMethod]
    public async Task Can_allow_prerelease_packages()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = ChainablePath.Current / "TestCases" / "Prerelease" / "SimpleApp.csproj";

        // Act
        var violations = await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings { ForceRestore = true }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "Apache-2.0", "Microsoft .NET Library License"],
                Prerelease = true,
            }
        });

        // Assert
        violations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Can_deny_prerelease_packages_using_an_allow_clause()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = ChainablePath.Current / "TestCases" / "Prerelease" / "SimpleApp.csproj";

        // Act
        var violations = await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings { ForceRestore = true }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "Apache-2.0", "Microsoft .NET Library License"],
                Prerelease = false,
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "FluentAssertions"
        });

        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "System.CommandLine"
        });
    }

    [TestMethod]
    public async Task Can_deny_prerelease_packages_using_a_deny_clause()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = ChainablePath.Current / "TestCases" / "Prerelease" / "SimpleApp.csproj";

        // Act
        var violations = await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings { ForceRestore = true }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "Apache-2.0", "Microsoft .NET Library License"],
            },
            DenyList = new DenyList
            {
                Prerelease = true,
            }
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "FluentAssertions"
        });

        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "System.CommandLine"
        });
    }

    [TestMethod]
    public async Task Can_exclude_an_entire_feed()
    {
        // Arrange
        var projectPath = ChainablePath.Current / "TestCases" / "UnreachableFeed";

        await File.WriteAllTextAsync(projectPath / "nuget.config",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        await Cli
            .Wrap("dotnet")
            .WithWorkingDirectory(projectPath)
            .WithArguments("restore --force")
            .ExecuteAsync();

        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var solutionPath = projectPath / "ConsoleApp.sln";

        // Act
        await File.WriteAllTextAsync(projectPath / "nuget.config",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="unreachable" value="https://someunreachablefeed" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var violations = await analyzer.ExecuteAnalysis(solutionPath, new AnalyzerSettings
        {
            SkipRestore = true
        }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit"],
            },
            IgnoredFeeds = ["unreachable"]
        });

        // Assert
        violations.Should().ContainEquivalentOf(new
        {
            PackageId = "FluentAssertions",
            Version = "8.3.0",
            License = "Unknown"
        });
    }

    [TestMethod]
    public async Task Creates_a_cache_if_asked_for()
    {
        // Arrange
        var current = ChainablePath.Current;
        (current / ".packageguard").DeleteFileOrDirectory();

        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = current / "TestCases" / "SimpleApp" / "SimpleApp.csproj";

        // Act
        var violations = await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings { UseCaching = true }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "Apache-2.0"],
            }
        });

        // Assert
        (current / ".packageguard" / "cache.bin").FileExists.Should().BeTrue();

        violations.Should().BeEquivalentTo(
        [
            new
            {
                License = "Microsoft .NET Library License",
                PackageId = "Microsoft.AspNet.WebApi.Client",
                Version = "6.0.0"
            }
        ]);
    }

    [TestMethod]
    public async Task Creates_a_cache_at_a_specific_path_if_asked_for()
    {
        // Arrange
        var current = ChainablePath.Current;
        (current / ".mycustomfolder").DeleteFileOrDirectory();

        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = current / "TestCases" / "SimpleApp" / "SimpleApp.csproj";

        // Act
        await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings
        {
            UseCaching = true,
            CacheFilePath = current / ".mycustomfolder" / "packageguard.cache"
        }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "Apache-2.0"],
            }
        });

        // Assert
        (current / ".mycustomfolder" / "packageguard.cache").FileExists.Should().BeTrue();
    }

    [TestMethod]
    public async Task Can_reuse_a_cache_from_an_earlier_run()
    {
        // Arrange
        var current = ChainablePath.Current;
        (current / ".packageguard").DeleteFileOrDirectory();

        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = current / "TestCases" / "SimpleApp" / "SimpleApp.csproj";

        // Do the first run to create the cache
        await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings { UseCaching = true }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "Apache-2.0"],
            }
        });

        // Act
        var loggingProvider = new InMemoryLoggerProvider();

        analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = loggingProvider.CreateLogger("")
        };

        await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings { UseCaching = true }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "Apache-2.0"],
            }
        });

        // Assert
        loggingProvider.Logs.Select(x => x.Message).Should().ContainMatch("*Successfully loaded the cache from*");
    }

    [TestMethod]
    public async Task Will_ignore_a_corrupt_cache()
    {
        // Arrange
        var current = ChainablePath.Current;
        var cacheFile = current / ".packageguard" / "cache.bin";

        cacheFile.DeleteFileOrDirectory();
        cacheFile.Directory.CreateDirectoryRecursively();

        await File.WriteAllTextAsync(cacheFile, "Some invalid content that is not a valid cache");

        var loggingProvider = new InMemoryLoggerProvider();
        // Act

        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = loggingProvider.CreateLogger("")
        };

        var projectPath = current / "TestCases" / "SimpleApp" / "SimpleApp.csproj";

        await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings { UseCaching = true }, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit", "Apache-2.0"],
            }
        });

        // Assert
        loggingProvider.Logs.Warnings.Select(x => x.Message).Should().ContainMatch("*Could not load package cache from*");
    }

    [TestMethod]
    public void Can_process_slnx_solution_files()
    {
        // Arrange & Act
        var slnxPath = ChainablePath.Current / "TestCases" / "SlnxApp" / "SlnxApp.slnx";
        var projects = cSharpProjectScanner.FindProjects(slnxPath);

        // Assert
        projects.Should().ContainSingle(p => p.EndsWith("SlnxApp.csproj"));
    }

    [TestMethod]
    public void Can_find_slnx_files_in_directory()
    {
        // Arrange & Act
        var directoryPath = ChainablePath.Current / "TestCases" / "SlnxApp";
        var projects = cSharpProjectScanner.FindProjects(directoryPath);

        // Assert
        projects.Should().ContainSingle(p => p.EndsWith("SlnxApp.csproj"));
    }
}
