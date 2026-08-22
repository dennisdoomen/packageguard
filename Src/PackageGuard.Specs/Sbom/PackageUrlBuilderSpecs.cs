using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Sbom;

namespace PackageGuard.Specs.Sbom;

[TestClass]
public class PackageUrlBuilderSpecs
{
    [TestMethod]
    public void Builds_a_nuget_purl()
    {
        string purl = PackageUrlBuilder.Build("nuget", "Newtonsoft.Json", "13.0.3");

        purl.Should().Be("pkg:nuget/Newtonsoft.Json@13.0.3");
    }

    [TestMethod]
    public void Builds_an_npm_purl()
    {
        string purl = PackageUrlBuilder.Build("npm", "lodash", "4.17.21");

        purl.Should().Be("pkg:npm/lodash@4.17.21");
    }

    [TestMethod]
    public void Encodes_the_scope_of_a_scoped_npm_package()
    {
        string purl = PackageUrlBuilder.Build("npm", "@types/node", "20.1.0");

        purl.Should().Be("pkg:npm/%40types/node@20.1.0");
    }

    [TestMethod]
    public void Encodes_prerelease_version_identifiers()
    {
        string purl = PackageUrlBuilder.Build("nuget", "Contoso.Beta", "1.0.0-beta.1+build");

        purl.Should().Be("pkg:nuget/Contoso.Beta@1.0.0-beta.1%2Bbuild");
    }
}
