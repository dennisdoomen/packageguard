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
                LegalRiskRationale =
                [
                    "Permissive license (MIT) (+0.0)",
                    "Valid license URL (+0.0)"
                ],
                SecurityRisk = 6.0,
                SecurityRiskRationale =
                [
                    "Public repository available (+0.0)",
                    "A security fix is available for a known vulnerability (+0.5)",
                    "OpenSSF Scorecard score is low (4.0) (+1.5)"
                ],
                OperationalRisk = 4.0,
                OperationalRiskRationale =
                [
                    "README looks present and non-default (+0.0)",
                    "CONTRIBUTING guide is present (+0.0)",
                    "CHANGELOG or release notes are present (+0.0)",
                    "Contributor count is healthy (5) (+0.0)"
                ]
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
        html.Should().Contain("<a href=\"https://github.com/contoso/contoso-security/blob/HEAD/LICENSE\" target=\"_blank\" rel=\"noreferrer noopener\">Valid license URL (+0.0)</a>");
        html.Should().Contain("<a href=\"https://github.com/contoso/contoso-security\" target=\"_blank\" rel=\"noreferrer noopener\">Public repository available (+0.0)</a>");
        html.Should().Contain("<a href=\"https://securityscorecards.dev/viewer/?uri=github.com/contoso/contoso-security\" target=\"_blank\" rel=\"noreferrer noopener\">OpenSSF Scorecard score is low (4.0) (+1.5)</a>");
        html.Should().Contain("<a href=\"https://github.com/contoso/contoso-security#readme\" target=\"_blank\" rel=\"noreferrer noopener\">README looks present and non-default (+0.0)</a>");
        html.Should().Contain("<a href=\"https://github.com/contoso/contoso-security/blob/HEAD/CONTRIBUTING.md\" target=\"_blank\" rel=\"noreferrer noopener\">CONTRIBUTING guide is present (+0.0)</a>");
        html.Should().Contain("<a href=\"https://github.com/contoso/contoso-security/releases\" target=\"_blank\" rel=\"noreferrer noopener\">CHANGELOG or release notes are present (+0.0)</a>");
        html.Should().NotContain("Relevant links");
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

    [TestMethod]
    public async Task Should_distinguish_missing_and_unrecognized_licenses_in_detailed_report()
    {
        var missingLicensePackage = new PackageInfo
        {
            Name = "Contoso.MissingLicense",
            Version = "1.0.0",
            Source = "nuget",
            License = "Unknown",
            RiskDimensions = new RiskDimensions()
        };

        var customLicensePackage = new PackageInfo
        {
            Name = "Contoso.CustomLicense",
            Version = "1.0.0",
            Source = "nuget",
            License = "Contoso-Internal-License",
            RiskDimensions = new RiskDimensions()
        };

        RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [missingLicensePackage, customLicensePackage]);

        string html = await File.ReadAllTextAsync(reportPaths.HtmlPath);
        html.Should().Contain("<span class=\"label\">License:</span> Missing or undetermined");
        html.Should().Contain(
            "<span class=\"label\">License:</span> Present, but not one of PackageGuard&#39;s well-known license IDs: Contoso-Internal-License");
    }

    [TestMethod]
    public async Task Should_write_generated_report_names_inside_explicit_directory()
    {
        string explicitDirectory = Path.Combine(reportDirectory, "custom-output");

        RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [new PackageInfo { Name = "Contoso.Security", Version = "2.4.0", RiskDimensions = new RiskDimensions() }],
            explicitDirectory);

        Directory.Exists(explicitDirectory).Should().BeTrue();
        Path.GetDirectoryName(reportPaths.HtmlPath).Should().Be(explicitDirectory);
        Path.GetDirectoryName(reportPaths.SarifPath).Should().Be(explicitDirectory);
        Path.GetFileName(reportPaths.HtmlPath).Should().StartWith("PackageGuard-risk-report-");
        Path.GetExtension(reportPaths.HtmlPath).Should().Be(".html");
        Path.GetExtension(reportPaths.SarifPath).Should().Be(".sarif");
    }

    [TestMethod]
    public async Task Should_use_explicit_report_file_name_and_overwrite_existing_reports()
    {
        string explicitHtmlPath = Path.Combine(reportDirectory, "named-output", "custom-risk-report.html");
        string explicitSarifPath = Path.ChangeExtension(explicitHtmlPath, ".sarif");
        Directory.CreateDirectory(Path.GetDirectoryName(explicitHtmlPath)!);
        await File.WriteAllTextAsync(explicitHtmlPath, "old html");
        await File.WriteAllTextAsync(explicitSarifPath, "old sarif");

        RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [new PackageInfo { Name = "Contoso.Security", Version = "2.4.0", RiskDimensions = new RiskDimensions() }],
            explicitHtmlPath);

        reportPaths.HtmlPath.Should().Be(explicitHtmlPath);
        reportPaths.SarifPath.Should().Be(explicitSarifPath);
        (await File.ReadAllTextAsync(explicitHtmlPath)).Should().NotBe("old html");
        (await File.ReadAllTextAsync(explicitSarifPath)).Should().NotBe("old sarif");
    }
}
