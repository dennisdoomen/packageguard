using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using PackageGuard.Core.Sbom;

namespace PackageGuard.Specs.Sbom;

[TestClass]
public class CycloneDxSbomWriterSpecs
{
    [TestMethod]
    public void Writes_the_bom_header_the_root_component_and_every_package_as_a_component_with_a_purl()
    {
        var direct = new PackageInfo
        {
            Name = "Contoso.Direct",
            Version = "1.0.0",
            Source = "nuget",
            License = "MIT",
            LicenseEvidence = LicenseEvidence.Declared,
            DependencyDepth = 1,
            DependencyKeys = [PackageInfo.CreatePackageKey("Contoso.Transitive", "2.0.0")]
        };

        var transitive = new PackageInfo
        {
            Name = "Contoso.Transitive",
            Version = "2.0.0",
            Source = "nuget",
            License = "Apache-2.0",
            LicenseEvidence = LicenseEvidence.Concluded,
            DependencyDepth = 2
        };

        SbomModel model = SbomModelBuilder.Build([direct, transitive], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(CycloneDxSbomWriter.Build(model));
        JsonElement root = document.RootElement;

        root.GetProperty("bomFormat").GetString().Should().Be("CycloneDX");
        root.GetProperty("specVersion").GetString().Should().Be("1.6");
        root.GetProperty("metadata").GetProperty("component").GetProperty("name").GetString().Should().Be("Contoso");

        JsonElement[] components = root.GetProperty("components").EnumerateArray().ToArray();
        components.Should().HaveCount(2);

        JsonElement directComponent = components.Single(c => c.GetProperty("name").GetString() == "Contoso.Direct");
        directComponent.GetProperty("purl").GetString().Should().Be("pkg:nuget/Contoso.Direct@1.0.0");
        directComponent.GetProperty("scope").GetString().Should().Be("required");
        directComponent.GetProperty("licenses")[0].GetProperty("license").GetProperty("id").GetString().Should().Be("MIT");
        directComponent.GetProperty("licenses")[0].GetProperty("license").GetProperty("acknowledgement").GetString()
            .Should().Be("declared");

        JsonElement transitiveComponent = components.Single(c => c.GetProperty("name").GetString() == "Contoso.Transitive");
        transitiveComponent.TryGetProperty("scope", out _).Should().BeFalse("transitive dependencies omit the scope field");
        transitiveComponent.GetProperty("licenses")[0].GetProperty("license").GetProperty("acknowledgement").GetString()
            .Should().Be("concluded");
    }

    [TestMethod]
    public void Attaches_direct_dependencies_to_the_root_and_records_known_transitive_edges()
    {
        var direct = new PackageInfo
        {
            Name = "Contoso.Direct",
            Version = "1.0.0",
            Source = "nuget",
            DependencyDepth = 1,
            DependencyKeys = [PackageInfo.CreatePackageKey("Contoso.Transitive", "2.0.0")]
        };

        var transitive = new PackageInfo
        {
            Name = "Contoso.Transitive",
            Version = "2.0.0",
            Source = "nuget",
            DependencyDepth = 2
        };

        SbomModel model = SbomModelBuilder.Build([direct, transitive], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(CycloneDxSbomWriter.Build(model));
        JsonElement[] dependencies = document.RootElement.GetProperty("dependencies").EnumerateArray().ToArray();

        JsonElement rootEntry = dependencies.Single(d => d.GetProperty("ref").GetString() == "solution-root");
        rootEntry.GetProperty("dependsOn")[0].GetString().Should().Be("pkg:nuget/Contoso.Direct@1.0.0");

        JsonElement directEntry = dependencies.Single(d => d.GetProperty("ref").GetString() == "pkg:nuget/Contoso.Direct@1.0.0");
        directEntry.GetProperty("dependsOn")[0].GetString().Should().Be("pkg:nuget/Contoso.Transitive@2.0.0");
    }

    [TestMethod]
    public void Omits_the_vulnerabilities_section_when_no_package_carries_vulnerability_data()
    {
        var package = new PackageInfo { Name = "Contoso.Package", Version = "1.0.0", Source = "nuget" };

        SbomModel model = SbomModelBuilder.Build([package], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(CycloneDxSbomWriter.Build(model));

        document.RootElement.TryGetProperty("vulnerabilities", out _).Should().BeFalse();
    }

    [TestMethod]
    public void Includes_a_vulnerabilities_section_when_a_package_carries_osv_data()
    {
        var package = new PackageInfo
        {
            Name = "Contoso.Package",
            Version = "1.0.0",
            Source = "nuget",
            Vulnerabilities =
            [
                new OsvVulnerabilityRecord
                {
                    Id = "GHSA-xxxx-xxxx-xxxx",
                    Severity = 7.5,
                    References = ["https://github.com/advisories/GHSA-xxxx-xxxx-xxxx"]
                }
            ]
        };

        SbomModel model = SbomModelBuilder.Build([package], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(CycloneDxSbomWriter.Build(model));
        JsonElement vulnerability = document.RootElement.GetProperty("vulnerabilities")[0];

        vulnerability.GetProperty("id").GetString().Should().Be("GHSA-xxxx-xxxx-xxxx");
        vulnerability.GetProperty("ratings")[0].GetProperty("score").GetDouble().Should().Be(7.5);
        vulnerability.GetProperty("affects")[0].GetProperty("ref").GetString().Should().Be("pkg:nuget/Contoso.Package@1.0.0");
    }

    [TestMethod]
    public void Describes_the_generating_tool_using_manufacturer_instead_of_the_removed_vendor_field()
    {
        var package = new PackageInfo { Name = "Contoso.Package", Version = "1.0.0", Source = "nuget" };

        SbomModel model = SbomModelBuilder.Build([package], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(CycloneDxSbomWriter.Build(model));
        JsonElement tool = document.RootElement.GetProperty("metadata").GetProperty("tools").GetProperty("components")[0];

        tool.GetProperty("manufacturer").GetProperty("name").GetString().Should().Be("Dennis Doomen");
        tool.TryGetProperty("vendor", out _).Should().BeFalse(
            "CycloneDX 1.6 removed the 'vendor' component field in favor of 'manufacturer'");
    }

    [TestMethod]
    public void Notes_the_flat_dependency_graph_limitation_for_npm_family_ecosystems()
    {
        var package = new PackageInfo { Name = "left-pad", Version = "1.0.0", Source = "npm" };

        SbomModel model = SbomModelBuilder.Build([package], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(CycloneDxSbomWriter.Build(model));
        JsonElement[] properties = document.RootElement.GetProperty("metadata").GetProperty("properties").EnumerateArray().ToArray();

        properties.Should().ContainSingle(p => p.GetProperty("name").GetString() == "packageguard:flat-dependency-graph");
    }
}
