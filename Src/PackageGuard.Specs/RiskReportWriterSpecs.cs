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
            LicenseUrl = "https://licenses.nuget.org/MIT",
            RepositoryUrl = "https://github.com/contoso/contoso-security",
            RiskScore = 47.5,
            OpenSsfScore = 8.2,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasReleaseNotes = true,
            HasRecentSuccessfulWorkflowRun = true,
            ReadmeUpdatedAt = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero),
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
        package.TrackAsUsedInProject(@"src\Contoso.App\Contoso.App.csproj");
        package.TrackAsUsedInProject(@"frontend\package.json");

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
        html.Should().Contain(@"src\Contoso.App\Contoso.App.csproj");
        html.Should().Contain(@"frontend\package.json");
        html.Should().Contain("<span class=\"label\">Used by:</span>");
        html.Should().NotContain("<th>Used by</th>");
        html.Should().Contain("Relevant links");
        html.Should().Contain("href=\"https://licenses.nuget.org/MIT\"");
        html.Should().Contain("href=\"https://securityscorecards.dev/viewer/?uri=github.com/contoso/contoso-security\"");
        html.Should().Contain("href=\"https://github.com/contoso/contoso-security/security\"");
        html.Should().Contain("href=\"https://github.com/contoso/contoso-security/blob/HEAD/CONTRIBUTING.md\"");
        html.Should().Contain("href=\"https://github.com/contoso/contoso-security/releases\"");
        html.Should().Contain("href=\"https://github.com/contoso/contoso-security/actions\"");
        html.Should().Contain("href=\"https://github.com/contoso/contoso-security#readme\"");
        html.Should().NotContain("<span class=\"label\">License URL:</span>");
        html.Should().NotContain("<span class=\"label\">Repository:</span>");
        html.Should().NotContain("<span class=\"label\">OpenSSF Scorecard:</span>");

        using JsonDocument sarif = JsonDocument.Parse(await File.ReadAllTextAsync(reportPaths.SarifPath));
        sarif.RootElement.GetProperty("version").GetString().Should().Be("2.1.0");
        JsonElement result = sarif.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        result.GetProperty("ruleId").GetString().Should().Be("packageguard/risk-medium");
        result.GetProperty("message").GetProperty("text").GetString().Should().Contain("Contoso.Security 2.4.0 scored 47.5/100");
        result.GetProperty("message").GetProperty("text").GetString().Should().Contain(@"Used by: frontend\package.json, src\Contoso.App\Contoso.App.csproj.");
        result.GetProperty("properties").GetProperty("usedBy")[0].GetString().Should().Be(@"frontend\package.json");
        result.GetProperty("properties").GetProperty("usedBy")[1].GetString().Should().Be(@"src\Contoso.App\Contoso.App.csproj");
        result.GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("artifactLocation")
            .GetProperty("uri")
            .GetString()
            .Should()
            .Be("PackageGuard.sln");
    }
}
