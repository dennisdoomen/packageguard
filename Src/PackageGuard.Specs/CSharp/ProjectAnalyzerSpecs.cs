using System;
using System.IO;
using System.Threading.Tasks;
using CliWrap;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using Pathy;

namespace PackageGuard.Specs.CSharp;

[TestClass]
public class ProjectAnalyzerSpecs
{
    private readonly LicenseFetcher licenseFetcher =
        new(NullLogger.Instance, Environment.GetEnvironmentVariable("GITHUB_API_KEY"));

    [TestMethod]
    public async Task Can_be_disabled()
    {
        // Arrange
        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = ChainablePath.Current / "NonExistingFolder" / "NonExisting.csproj";

        AnalyzerSettings analyzerSettings = new()
        {
            ScanNuGet = false
        };

        // Act
        var act = () => analyzer.ExecuteAnalysis(projectPath, analyzerSettings, _ => new ProjectPolicy
        {
            AllowList = new AllowList
            {
                Licenses = ["mit"]
            }
        });

        // Assert
        await act.Should().NotThrowAsync();
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
        var violations = await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings
        {
            ForceRestore = true
        }, _ => new ProjectPolicy
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
        var violations = await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings
        {
            ForceRestore = true
        }, _ => new ProjectPolicy
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
        var violations = await analyzer.ExecuteAnalysis(projectPath, new AnalyzerSettings
        {
            ForceRestore = true
        }, _ => new ProjectPolicy
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
}
