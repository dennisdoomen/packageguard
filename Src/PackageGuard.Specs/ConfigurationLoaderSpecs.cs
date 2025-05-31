using System;
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
                "Settings": {
                    "Allow": {
                        "Packages": [
                            "PackageGuard/1.2.3"
                        ],
                        "Licenses": [
                            "MIT"
                        ],
                        "Feeds": [
                            "https://api.nuget.org/v3/index.json"
                        ]
                    },
                    "Deny": {
                        "Packages": [
                            "Bogus/Package"
                        ],
                        "Licenses": [
                            "Proprietary"
                        ]
                    }
                }
            }
            """);

        // Act
        ConfigurationLoader.Configure(analyzer, Environment.CurrentDirectory, "test.json");

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
            }
        });
    }
}
