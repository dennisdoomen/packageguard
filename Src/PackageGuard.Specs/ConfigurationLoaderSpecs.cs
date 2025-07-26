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
}
