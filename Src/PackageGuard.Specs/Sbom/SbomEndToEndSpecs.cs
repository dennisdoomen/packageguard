using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using PackageGuard.Core.Sbom;
using Pathy;

namespace PackageGuard.Specs.Sbom;

[TestClass]
public class SbomEndToEndSpecs
{
    private readonly LicenseFetcher licenseFetcher =
        new(NullLogger.Instance, Environment.GetEnvironmentVariable("GITHUB_API_KEY"));

    [TestMethod]
    public async Task Produces_a_parseable_cyclonedx_and_spdx_document_from_a_resolved_nuget_project_without_fetching_risk_data()
    {
        var analyzer = new ProjectAnalyzer(licenseFetcher);
        var projectPath = ChainablePath.Current / "TestCases" / "SimpleApp" / "SimpleApp.csproj";

        AnalysisResult result = await analyzer.ExecuteAnalysisWithRisk(projectPath, new AnalyzerSettings
        {
            ForceRestore = true
        }, _ => new ProjectPolicy { AllowList = new AllowList { Licenses = ["mit"] } });

        result.Packages.Should().NotBeEmpty();
        result.Packages.Should().OnlyContain(package => package.Vulnerabilities.Length == 0,
            "SBOM generation without --report-risk must never populate vulnerability data");

        SbomModel model = SbomModelBuilder.Build(result.Packages, projectPath);

        using JsonDocument cyclonedx = JsonDocument.Parse(CycloneDxSbomWriter.Build(model));
        cyclonedx.RootElement.GetProperty("bomFormat").GetString().Should().Be("CycloneDX");
        cyclonedx.RootElement.GetProperty("components").GetArrayLength().Should().Be(result.Packages.Length);
        cyclonedx.RootElement.GetProperty("components").EnumerateArray()
            .Should().OnlyContain(c => c.GetProperty("purl").GetString()!.StartsWith("pkg:nuget/"));
        cyclonedx.RootElement.TryGetProperty("vulnerabilities", out _).Should().BeFalse();

        using JsonDocument spdx = JsonDocument.Parse(SpdxSbomWriter.Build(model));
        spdx.RootElement.GetProperty("spdxVersion").GetString().Should().Be("SPDX-2.3");
        spdx.RootElement.GetProperty("packages").GetArrayLength().Should().Be(result.Packages.Length + 1,
            "the synthetic root package plus one entry per resolved package");
    }

    [TestMethod]
    public void Creates_missing_parent_directories_for_the_sbom_output_path()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "PackageGuard-SbomSpecs", Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(outputDirectory, "nested", "bom.json");
        Directory.Exists(outputDirectory).Should().BeFalse("the test must start from a directory that doesn't exist yet");

        try
        {
            var package = new PackageInfo { Name = "Contoso.Package", Version = "1.0.0", Source = "nuget" };

            SbomWriter.Write([package], "", "cyclonedx", outputPath);

            File.Exists(outputPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }
}
