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
    [DataRow("Microsoft.AspNet.WebApi.Client")]
    [DataRow("Microsoft.AspNet.WebApi.Core")]
    [DataRow("Microsoft.AspNet.WebApi.WebHost")]
    [DataRow("Microsoft.AspNet.WebApi.Owin")]
    [DataRow("Microsoft.AspNet.WebApi.OwinSelfHost")]
    public async Task Can_understand_microsoft_aspnet_libraries(string name)
    {
        // Arrange
        var analyzer = new NuGetPackageAnalyzer(nullLogger, new LicenseFetcher(nullLogger));
        var packages = new PackageInfoCollection();

        // Act
        await analyzer.CollectPackageMetadata(ChainablePath.Current.Parent.Parent, name, NuGetVersion.Parse("6.0.0"), packages);

        // Assert
        packages.Should().ContainSingle(x => x.License == "Microsoft .NET Library License");
    }
}
