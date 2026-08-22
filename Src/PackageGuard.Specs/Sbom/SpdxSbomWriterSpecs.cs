using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using PackageGuard.Core.Package;
using PackageGuard.Core.Sbom;

namespace PackageGuard.Specs.Sbom;

[TestClass]
public class SpdxSbomWriterSpecs
{
    /// <summary>
    /// SPDX 2.3's required timestamp format: no fractional seconds and a literal 'Z' rather than a
    /// '+00:00'-style offset.
    /// </summary>
    private static readonly Regex SpdxTimestampPattern = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$");

    [TestMethod]
    public void Writes_the_document_header_and_a_package_with_its_purl_as_an_external_ref()
    {
        var package = new PackageInfo
        {
            Name = "Contoso.Package",
            Version = "1.0.0",
            Source = "nuget",
            License = "MIT",
            LicenseEvidence = LicenseEvidence.Declared,
            DependencyDepth = 1
        };

        SbomModel model = SbomModelBuilder.Build([package], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(SpdxSbomWriter.Build(model));
        JsonElement root = document.RootElement;

        root.GetProperty("spdxVersion").GetString().Should().Be("SPDX-2.3");
        root.GetProperty("name").GetString().Should().Be("Contoso");

        JsonElement[] packages = root.GetProperty("packages").EnumerateArray().ToArray();
        packages.Should().HaveCount(2, "the synthetic root package plus the one resolved package");

        JsonElement resolvedPackage = packages.Single(p => p.GetProperty("name").GetString() == "Contoso.Package");
        resolvedPackage.GetProperty("licenseDeclared").GetString().Should().Be("MIT");
        resolvedPackage.GetProperty("licenseConcluded").GetString().Should().Be("NOASSERTION");
        resolvedPackage.GetProperty("externalRefs")[0].GetProperty("referenceType").GetString().Should().Be("purl");
        resolvedPackage.GetProperty("externalRefs")[0].GetProperty("referenceLocator").GetString()
            .Should().Be("pkg:nuget/Contoso.Package@1.0.0");
    }

    [TestMethod]
    public void Records_a_concluded_license_separately_from_a_declared_one()
    {
        var package = new PackageInfo
        {
            Name = "Contoso.Package",
            Version = "1.0.0",
            Source = "nuget",
            License = "Apache-2.0",
            LicenseEvidence = LicenseEvidence.Concluded
        };

        SbomModel model = SbomModelBuilder.Build([package], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(SpdxSbomWriter.Build(model));
        JsonElement resolvedPackage = document.RootElement.GetProperty("packages")
            .EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Contoso.Package");

        resolvedPackage.GetProperty("licenseDeclared").GetString().Should().Be("NOASSERTION");
        resolvedPackage.GetProperty("licenseConcluded").GetString().Should().Be("Apache-2.0");
    }

    [TestMethod]
    public void Describes_the_root_package_and_relates_direct_and_transitive_dependencies()
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

        using JsonDocument document = JsonDocument.Parse(SpdxSbomWriter.Build(model));
        JsonElement[] relationships = document.RootElement.GetProperty("relationships").EnumerateArray().ToArray();

        relationships.Should().ContainSingle(r =>
            r.GetProperty("relationshipType").GetString() == "DESCRIBES" &&
            r.GetProperty("spdxElementId").GetString() == "SPDXRef-DOCUMENT");

        relationships.Count(r => r.GetProperty("relationshipType").GetString() == "DEPENDS_ON").Should().Be(2,
            "the root depends on the direct package, which in turn depends on the transitive package");
    }

    [TestMethod]
    public void Summarizes_known_vulnerabilities_as_a_package_annotation()
    {
        var package = new PackageInfo
        {
            Name = "Contoso.Package",
            Version = "1.0.0",
            Source = "nuget",
            Vulnerabilities = [new OsvVulnerabilityRecord { Id = "GHSA-xxxx-xxxx-xxxx", Severity = 9.1 }]
        };

        SbomModel model = SbomModelBuilder.Build([package], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(SpdxSbomWriter.Build(model));
        JsonElement resolvedPackage = document.RootElement.GetProperty("packages")
            .EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Contoso.Package");

        resolvedPackage.GetProperty("annotations")[0].GetProperty("comment").GetString().Should().Contain("GHSA-xxxx-xxxx-xxxx");
        resolvedPackage.GetProperty("annotations")[0].GetProperty("annotationDate").GetString()
            .Should().MatchRegex(SpdxTimestampPattern.ToString());
    }

    [TestMethod]
    public void Writes_timestamps_in_the_strict_spdx_format_without_fractional_seconds_or_an_offset()
    {
        var package = new PackageInfo { Name = "Contoso.Package", Version = "1.0.0", Source = "nuget" };

        SbomModel model = SbomModelBuilder.Build([package], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(SpdxSbomWriter.Build(model));
        string created = document.RootElement.GetProperty("creationInfo").GetProperty("created").GetString();

        created.Should().MatchRegex(SpdxTimestampPattern.ToString(),
            "SPDX 2.3 requires 'YYYY-MM-DDThh:mm:ssZ', not .NET's default round-trip format with fractional seconds and a '+00:00' offset");
    }

    [TestMethod]
    public void Notes_the_flat_dependency_graph_limitation_for_npm_family_ecosystems_as_a_document_comment()
    {
        var package = new PackageInfo { Name = "left-pad", Version = "1.0.0", Source = "npm" };

        SbomModel model = SbomModelBuilder.Build([package], "Contoso.sln");

        using JsonDocument document = JsonDocument.Parse(SpdxSbomWriter.Build(model));

        document.RootElement.GetProperty("comment").GetString().Should().Contain("npm");
    }
}
