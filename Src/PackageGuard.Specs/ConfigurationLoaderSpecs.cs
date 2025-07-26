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
                Feeds = new[] { "https://api.nuget.org/v3/index.json" }
            },
            DenyList = new
            {
                Packages = new[]
                {
                    new PackageSelector("Bogus", "Package")
                },
                Licenses = new[] { "Proprietary" }
            },
            IgnoredFeeds = new[]
            {
                "https://api.nuget.org/v3/index.json"
            }
        });
    }

    [TestMethod]
    public void Can_configure_deny_prerelease_packages()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        File.WriteAllText(ChainablePath.Current / "prerelease-test.json",
            """
            {
                "settings": {
                    "deny": {
                        "prerelease": true,
                        "packages": [],
                        "licenses": []
                    }
                }
            }
            """);

        // Act
        ConfigurationLoader.Configure(analyzer, "prerelease-test.json");

        // Assert
        analyzer.DenyList.Prerelease.Should().BeTrue();
    }

    [TestMethod]
    public void Can_parse_the_updated_configuration_file_with_prerelease()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        File.WriteAllText(ChainablePath.Current / "comprehensive-test.json",
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
        ConfigurationLoader.Configure(analyzer, "comprehensive-test.json");

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
                Feeds = new[] { "https://api.nuget.org/v3/index.json" }
            },
            DenyList = new
            {
                Prerelease = true,
                Packages = new[]
                {
                    new PackageSelector("Bogus", "Package")
                },
                Licenses = new[] { "Proprietary" }
            },
            IgnoredFeeds = new[]
            {
                "https://api.nuget.org/v3/index.json"
            }
        });
    }

    [TestMethod]
    public void Can_configure_allow_prerelease_packages()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        File.WriteAllText(ChainablePath.Current / "allow-prerelease-test.json",
            """
            {
                "settings": {
                    "allow": {
                        "prerelease": true,
                        "packages": [],
                        "licenses": [],
                        "feeds": []
                    }
                }
            }
            """);

        // Act
        ConfigurationLoader.Configure(analyzer, "allow-prerelease-test.json");

        // Assert
        analyzer.AllowList.Prerelease.Should().BeTrue();
    }

    [TestMethod]
    public void Can_parse_the_comprehensive_configuration_with_both_allow_and_deny_prerelease()
    {
        // Arrange
        var analyzer = new CSharpProjectAnalyzer(
            A.Fake<CSharpProjectScanner>(),
            new NuGetPackageAnalyzer(A.Fake<ILogger>(), new LicenseFetcher(A.Fake<ILogger>())));

        File.WriteAllText(ChainablePath.Current / "full-prerelease-test.json",
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
                        "prerelease": false,
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
        ConfigurationLoader.Configure(analyzer, "full-prerelease-test.json");

        // Assert
        analyzer.Should().BeEquivalentTo(new
        {
            AllowList = new
            {
                Prerelease = true,
                Packages = new[]
                {
                    new PackageSelector("PackageGuard", "1.2.3")
                },
                Licenses = new[] { "MIT" },
                Feeds = new[] { "https://api.nuget.org/v3/index.json" }
            },
            DenyList = new
            {
                Prerelease = false,
                Packages = new[]
                {
                    new PackageSelector("Bogus", "Package")
                },
                Licenses = new[] { "Proprietary" }
            },
            IgnoredFeeds = new[]
            {
                "https://api.nuget.org/v3/index.json"
            }
        });
    }
}
