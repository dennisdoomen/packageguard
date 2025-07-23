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
                            "Bogus/Package",
                            "MyPackage/(,1.0.0)",
                            "TestPackage/2.0.0-alpha.1"
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
                    new PackageSelector("Bogus", "Package"),
                    new PackageSelector("MyPackage", "(,1.0.0)"),
                    new PackageSelector("TestPackage", "2.0.0-alpha.1")
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
