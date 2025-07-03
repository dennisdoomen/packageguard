using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Versioning;
using PackageGuard.Core;
using Pathy;

namespace PackageGuard.Specs;

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
}
