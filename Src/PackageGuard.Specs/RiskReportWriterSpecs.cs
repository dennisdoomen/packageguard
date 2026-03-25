using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
internal sealed class RiskReportWriterSpecs
{
    private string reportDirectory = null!;
    private string originalReportDirectory = "";
    private bool hadOriginalReportDirectory;

    [TestInitialize]
    public void SetUp()
    {
        reportDirectory = Path.Combine(Path.GetTempPath(), "PackageGuard-ReportSpecs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(reportDirectory);
        string existingReportDirectory = Environment.GetEnvironmentVariable(RiskHtmlReportWriter.ReportDirectoryEnvironmentVariable);
        hadOriginalReportDirectory = existingReportDirectory is not null;
        originalReportDirectory = existingReportDirectory ?? "";
        Environment.SetEnvironmentVariable(RiskHtmlReportWriter.ReportDirectoryEnvironmentVariable, reportDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(
            RiskHtmlReportWriter.ReportDirectoryEnvironmentVariable,
            hadOriginalReportDirectory ? originalReportDirectory : null);

        if (Directory.Exists(reportDirectory))
        {
            Directory.Delete(reportDirectory, true);
        }
    }

    [TestMethod]
    public async Task Should_write_companion_html_and_sarif_reports()
    {
        var package = new PackageInfo
        {
            Name = "Contoso.Security",
            Version = "2.4.0",
            Source = "nuget",
            License = "MIT",
            RiskScore = 47.5,
            RiskDimensions = new RiskDimensions
            {
                LegalRisk = 1.0,
                LegalRiskRationale = ["Permissive license (MIT) (+0.0)"],
                SecurityRisk = 6.0,
                SecurityRiskRationale = ["A security fix is available for a known vulnerability (+0.5)"],
                OperationalRisk = 4.0,
                OperationalRiskRationale = ["Contributor count is healthy (5) (+0.0)"]
            }
        };

        RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [package]);

        File.Exists(reportPaths.HtmlPath).Should().BeTrue();
        File.Exists(reportPaths.SarifPath).Should().BeTrue();
        Path.GetDirectoryName(reportPaths.HtmlPath).Should().Be(reportDirectory);
        Path.GetDirectoryName(reportPaths.SarifPath).Should().Be(reportDirectory);
        Path.GetFileNameWithoutExtension(reportPaths.HtmlPath)
            .Should()
            .Be(Path.GetFileNameWithoutExtension(reportPaths.SarifPath));

        string html = await File.ReadAllTextAsync(reportPaths.HtmlPath);
        html.Should().Contain("PackageGuard Risk Report");
        html.Should().Contain("Contoso.Security");

        using JsonDocument sarif = JsonDocument.Parse(await File.ReadAllTextAsync(reportPaths.SarifPath));
        sarif.RootElement.GetProperty("version").GetString().Should().Be("2.1.0");
        JsonElement result = sarif.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        result.GetProperty("ruleId").GetString().Should().Be("packageguard/risk-medium");
        result.GetProperty("message").GetProperty("text").GetString().Should().Contain("Contoso.Security 2.4.0 scored 47.5/100");
        result.GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("artifactLocation")
            .GetProperty("uri")
            .GetString()
            .Should()
            .Be("PackageGuard.sln");
    }
}
