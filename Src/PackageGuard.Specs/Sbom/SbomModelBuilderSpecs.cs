using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using PackageGuard.Core.Sbom;

namespace PackageGuard.Specs.Sbom;

[TestClass]
public class SbomModelBuilderSpecs
{
    [TestMethod]
    public void Builds_a_purl_and_marks_direct_dependencies_for_nuget_packages()
    {
        var direct = new PackageInfo
        {
            Name = "Contoso.Direct",
            Version = "1.0.0",
            Source = "nuget",
            SourceUrl = "https://api.nuget.org/v3/index.json",
            DependencyDepth = 1,
            DependencyKeys = [PackageInfo.CreatePackageKey("Contoso.Transitive", "2.0.0")]
        };

        var transitive = new PackageInfo
        {
            Name = "Contoso.Transitive",
            Version = "2.0.0",
            Source = "nuget",
            SourceUrl = "https://api.nuget.org/v3/index.json",
            DependencyDepth = 2
        };

        SbomModel model = SbomModelBuilder.Build([direct, transitive], "Contoso.sln");

        SbomComponent directComponent = model.Components.Single(c => c.Name == "Contoso.Direct");
        SbomComponent transitiveComponent = model.Components.Single(c => c.Name == "Contoso.Transitive");

        directComponent.Purl.Should().Be("pkg:nuget/Contoso.Direct@1.0.0");
        directComponent.IsDirect.Should().BeTrue();
        transitiveComponent.IsDirect.Should().BeFalse();

        model.Edges.Should().ContainSingle(edge =>
            edge.FromKey == directComponent.Key && edge.ToKey == transitiveComponent.Key);
    }

    [TestMethod]
    public void Marks_the_nuget_graph_as_accurate_and_the_npm_graph_as_flat()
    {
        var nugetPackage = new PackageInfo { Name = "A", Version = "1.0.0", Source = "nuget" };
        var npmPackage = new PackageInfo { Name = "b", Version = "1.0.0", Source = "npm" };

        SbomModel model = SbomModelBuilder.Build([nugetPackage, npmPackage], "solution");

        model.EcosystemGraphIsAccurate["nuget"].Should().BeTrue();
        model.EcosystemGraphIsAccurate["npm"].Should().BeFalse();
    }

    [TestMethod]
    public void Does_not_synthesize_edges_for_ecosystems_without_an_accurate_graph()
    {
        var parent = new PackageInfo
        {
            Name = "parent",
            Version = "1.0.0",
            Source = "npm",
            DependencyKeys = [PackageInfo.CreateDependencyKey("npm", "child", "1.0.0")]
        };

        var child = new PackageInfo { Name = "child", Version = "1.0.0", Source = "npm" };

        SbomModel model = SbomModelBuilder.Build([parent, child], "solution");

        model.Edges.Should().BeEmpty();
    }

    [TestMethod]
    public void Carries_license_evidence_and_vulnerabilities_onto_the_component()
    {
        var package = new PackageInfo
        {
            Name = "Contoso.Package",
            Version = "1.0.0",
            Source = "nuget",
            License = "MIT",
            LicenseEvidence = LicenseEvidence.Concluded,
            Vulnerabilities =
            [
                new OsvVulnerabilityRecord { Id = "GHSA-xxxx", Severity = 7.5 }
            ]
        };

        SbomModel model = SbomModelBuilder.Build([package], "solution");

        SbomComponent component = model.Components.Single();
        component.LicenseEvidence.Should().Be(LicenseEvidence.Concluded);
        component.Vulnerabilities.Should().ContainSingle().Which.Id.Should().Be("GHSA-xxxx");
    }

    [TestMethod]
    public void Resolves_the_root_component_name_from_the_solution_file_name_without_its_extension()
    {
        SbomModel model = SbomModelBuilder.Build([], @"C:\repo\Contoso.sln");

        model.Root.Name.Should().Be("Contoso");
    }
}
