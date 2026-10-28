using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Versioning;
using PackageGuard.Core;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Package;
using Pathy;

namespace PackageGuard.Specs.CSharp;

[TestClass]
public class NuGetPackageAnalyzerSpecs
{
    private readonly NullLogger nullLogger = NullLogger.Instance;

    [TestMethod]
    [DataRow("Microsoft.AspNet.WebApi.Client", "6.0.0")]
    [DataRow("Microsoft.AspNet.WebApi.Core", "5.3.0")]
    [DataRow("Microsoft.AspNet.WebApi.WebHost", "5.3.0")]
    [DataRow("Microsoft.AspNet.WebApi.Owin", "5.3.0")]
    [DataRow("Microsoft.AspNet.WebApi.OwinSelfHost", "5.3.0")]
    public async Task Can_understand_microsoft_aspnet_libraries(string name, string version)
    {
        // Arrange
        var analyzer = new NuGetPackageAnalyzer(nullLogger, new LicenseFetcher(nullLogger));
        var packages = new PackageInfoCollection(nullLogger);

        // Act
        await analyzer.CollectPackageMetadata(ChainablePath.Current.Parent.Parent, name, NuGetVersion.Parse(version), packages);

        // Assert
        packages.Should().ContainSingle(x => x.License == "Microsoft .NET Library License");
    }

    [TestMethod]
    public async Task Can_detect_nunit_mit_license()
    {
        // Arrange
        var analyzer = new NuGetPackageAnalyzer(nullLogger, new LicenseFetcher(nullLogger));
        var packages = new PackageInfoCollection(nullLogger);

        // Act
        await analyzer.CollectPackageMetadata(ChainablePath.Current.Parent.Parent, "NUnit", NuGetVersion.Parse("3.14.0"),
            packages);

        // Assert
        packages.Should().ContainSingle();
        var package = packages.First();
        package.License.Should().Be("MIT");
    }

    [TestMethod]
    public async Task Can_prefer_repository_metadata_over_project_url()
    {
        // Arrange
        var analyzer = new NuGetPackageAnalyzer(nullLogger, new LicenseFetcher(nullLogger));
        var packages = new PackageInfoCollection(nullLogger);

        // Act

        // Must match the FluentAssertions PackageReference version in this project, since the nuspec this test relies
        // on is only present in the local NuGet cache for the version that was actually restored.
        await analyzer.CollectPackageMetadata(ChainablePath.Current.Parent.Parent, "FluentAssertions",
            NuGetVersion.Parse("8.11.0"),
            packages);

        // Assert
        packages.Should().ContainSingle();
        packages.First().RepositoryUrl.Should().Be("https://github.com/fluentassertions/fluentassertions");
    }

    [TestMethod]
    public async Task Configure_credential_providers_when_analyzing_packages()
    {
        // Arrange
        var analyzer = new NuGetPackageAnalyzer(nullLogger, new LicenseFetcher(nullLogger));
        var packages = new PackageInfoCollection(nullLogger);

        // Act / Assert

        // This should not throw due to missing credential providers
        // We use a non-existent package to avoid actual network calls, but the credential provider setup should still occur
        await analyzer.CollectPackageMetadata(ChainablePath.Current.Parent.Parent, "NonExistentPackage.Test",
            NuGetVersion.Parse("1.0.0"), packages);
    }

    [TestMethod]
    public async Task Can_handle_multiple_concurrent_credential_provider_setups()
    {
        // Arrange
        var analyzer1 = new NuGetPackageAnalyzer(nullLogger, new LicenseFetcher(nullLogger));
        var analyzer2 = new NuGetPackageAnalyzer(nullLogger, new LicenseFetcher(nullLogger));
        var packages1 = new PackageInfoCollection(nullLogger);
        var packages2 = new PackageInfoCollection(nullLogger);

        // Act / Assert

        // Multiple analyzers should safely configure credential providers concurrently
        var task1 = analyzer1.CollectPackageMetadata(ChainablePath.Current.Parent.Parent, "NonExistentPackage.Test1",
            NuGetVersion.Parse("1.0.0"), packages1);

        var task2 = analyzer2.CollectPackageMetadata(ChainablePath.Current.Parent.Parent, "NonExistentPackage.Test2",
            NuGetVersion.Parse("1.0.0"), packages2);

        await Task.WhenAll(task1, task2);
    }

    [TestMethod]
    public async Task Skips_a_feed_that_fails_and_tries_the_next_one()
    {
        // Arrange
        ChainablePath projectPath = CreateProjectWithNuGetConfig(
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="broken" value="http://localhost:1/v3/index.json" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var analyzer = new NuGetPackageAnalyzer(nullLogger, new LicenseFetcher(nullLogger));
        var packages = new PackageInfoCollection(nullLogger);

        // Act
        await analyzer.CollectPackageMetadata(projectPath, "NUnit", NuGetVersion.Parse("3.14.0"), packages);

        // Assert
        packages.Should().ContainSingle(package => package.Source == "nuget.org");
    }

    [TestMethod]
    public async Task Only_queries_the_feeds_that_the_package_source_mapping_allows()
    {
        // Arrange
        ChainablePath projectPath = CreateProjectWithNuGetConfig(
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="http://localhost:1/v3/index.json" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="private">
                  <package pattern="NUnit" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        var analyzer = new NuGetPackageAnalyzer(nullLogger, new LicenseFetcher(nullLogger));
        var packages = new PackageInfoCollection(nullLogger);

        // Act
        await analyzer.CollectPackageMetadata(projectPath, "NUnit", NuGetVersion.Parse("3.14.0"), packages);
        await analyzer.CollectPackageMetadata(projectPath, "Newtonsoft.Json", NuGetVersion.Parse("13.0.3"), packages);

        // Assert
        packages.Should().ContainSingle(package => package.Name == "Newtonsoft.Json",
            "NUnit is mapped to the unreachable private feed only, even though nuget.org has it");
    }

    private static ChainablePath CreateProjectWithNuGetConfig(string nuGetConfig)
    {
        ChainablePath projectPath = ChainablePath.Temp / Path.GetRandomFileName();
        Directory.CreateDirectory(projectPath);
        File.WriteAllText(projectPath / "nuget.config", nuGetConfig);

        return projectPath;
    }
}
