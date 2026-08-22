using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console;

namespace PackageGuard.Specs;

[TestClass]
public class AnalyzeCommandSettingsSpecs
{
    [TestMethod]
    public void Bare_report_risk_flag_enables_risk_reporting_without_an_explicit_path()
    {
        var settings = new AnalyzeCommandSettings
        {
            ReportRisk = true
        };

        settings.GetReportRiskPath().Should().BeNull();
        settings.ToCoreSettings().ReportRisk.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("cyclonedx")]
    [DataRow("CycloneDX")]
    [DataRow("spdx")]
    [DataRow("SPDX")]
    public void Accepts_a_recognized_sbom_format_when_an_output_path_is_given(string format)
    {
        var settings = new AnalyzeCommandSettings { Sbom = format, SbomOutput = "bom.json" };

        settings.Validate().Successful.Should().BeTrue();
    }

    [TestMethod]
    public void Rejects_an_unrecognized_sbom_format()
    {
        var settings = new AnalyzeCommandSettings { Sbom = "bogus", SbomOutput = "bom.json" };

        ValidationResult result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("--sbom");
    }

    [TestMethod]
    public void Requires_an_output_path_when_sbom_is_specified()
    {
        var settings = new AnalyzeCommandSettings { Sbom = "cyclonedx" };

        ValidationResult result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("--sbom-output");
    }

    [TestMethod]
    public void Does_not_require_an_sbom_format_or_output_path_when_sbom_generation_is_not_requested()
    {
        var settings = new AnalyzeCommandSettings();

        settings.Validate().Successful.Should().BeTrue();
    }
}
