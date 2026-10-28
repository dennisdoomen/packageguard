using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Package;
using PackageGuard.Core.Risk;

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
        string existingReportDirectory =
            Environment.GetEnvironmentVariable(RiskHtmlReportWriter.ReportDirectoryEnvironmentVariable);

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
                    "License URL (+0.0)"
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
        html.Should().Contain("<span class=\"score-pill risk-medium\">47.5/100</span>");
        html.Should().Contain("<span class=\"score-pill risk-low\">1/10</span>");
        html.Should().Contain("<span class=\"score-pill risk-high\">6/10</span>");
        html.Should().Contain("<span class=\"score-pill risk-medium\">4/10</span>");
        html.Should().Contain(@"src\Contoso.App\Contoso.App.csproj");
        html.Should().Contain(@"frontend\package.json");
        html.Should().Contain("<dt>Used by</dt>");
        html.Should().NotContain("<th>Used by</th>");
        html.Should()
            .Contain(
                "<a href=\"https://github.com/contoso/contoso-security/blob/HEAD/LICENSE\" target=\"_blank\" rel=\"noreferrer noopener\">License URL (+0.0)</a>");

        html.Should()
            .Contain(
                "<a href=\"https://github.com/contoso/contoso-security\" target=\"_blank\" rel=\"noreferrer noopener\">Public repository available (+0.0)</a>");

        html.Should()
            .Contain(
                "<a href=\"https://securityscorecards.dev/viewer/?uri=github.com/contoso/contoso-security\" target=\"_blank\" rel=\"noreferrer noopener\">OpenSSF Scorecard score is low (4.0) <strong>(+1.5)</strong></a>");

        html.Should().Contain("A security fix is available for a known vulnerability <strong>(+0.5)</strong>");
        html.Should().NotContain("Permissive license (MIT) <strong>(+0.0)</strong>");
        html.Should().Contain("Permissive license (MIT) (+0.0)");

        html.Should()
            .Contain(
                "<a href=\"https://github.com/contoso/contoso-security#readme\" target=\"_blank\" rel=\"noreferrer noopener\">README looks present and non-default (+0.0)</a>");

        html.Should()
            .Contain(
                "<a href=\"https://github.com/contoso/contoso-security/blob/HEAD/CONTRIBUTING.md\" target=\"_blank\" rel=\"noreferrer noopener\">CONTRIBUTING guide is present (+0.0)</a>");

        html.Should()
            .Contain(
                "<a href=\"https://github.com/contoso/contoso-security/releases\" target=\"_blank\" rel=\"noreferrer noopener\">CHANGELOG or release notes are present (+0.0)</a>");

        html.Should().NotContain("Relevant links");
        html.Should().NotContain("<dt>License URL</dt>");
        html.Should().NotContain("<dt>Repository</dt>");
        html.Should().NotContain("<dt>OpenSSF Scorecard</dt>");

        using JsonDocument sarif = JsonDocument.Parse(await File.ReadAllTextAsync(reportPaths.SarifPath));
        sarif.RootElement.GetProperty("version").GetString().Should().Be("2.1.0");
        JsonElement result = sarif.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        result.GetProperty("ruleId").GetString().Should().Be("packageguard/risk-medium");
        result.GetProperty("message").GetProperty("text").GetString().Should().Contain("Contoso.Security 2.4.0 scored 47.5/100");
        result.GetProperty("message").GetProperty("text").GetString().Should()
            .Contain(@"Used by: frontend\package.json, src\Contoso.App\Contoso.App.csproj.");

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
    public async Task Should_include_vulnerability_and_transitive_dependency_detail_lists_in_evidence_section()
    {
        var package = new PackageInfo
        {
            Name = "Contoso.Evidence",
            Version = "1.0.0",
            License = "MIT",
            VulnerabilityCount = 1,
            MaxVulnerabilitySeverity = 8.0,
            Vulnerabilities = [new OsvVulnerabilityRecord { Id = "GHSA-aaaa-bbbb-cccc" }],
            TransitiveVulnerabilityCount = 1,
            VulnerableTransitiveDependencyDetails = ["Vulnerable.Dependency 2.0.0 (GHSA-dddd-eeee-ffff)"],
            StaleTransitiveDependencyCount = 1,
            StaleTransitiveDependencyDetails = ["Stale.Package 1.2.3 (last release 2020-01-01)"],
            AbandonedTransitiveDependencyCount = 1,
            AbandonedTransitiveDependencyDetails = ["Abandoned.Package 1.2.3 (known vulnerabilities)"],
            DeprecatedTransitiveDependencyCount = 1,
            DeprecatedTransitiveDependencyDetails = ["Deprecated.Package 3.0.0"],
            UnmaintainedCriticalTransitiveDependencyCount = 1,
            UnmaintainedCriticalTransitiveDependencyDetails = ["Critical.Package 4.5.6 (max severity 9.0)"]
        };

        RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [package]);

        string html = await File.ReadAllTextAsync(reportPaths.HtmlPath);

        html.Should().MatchRegex(Evidence("Vulnerabilities", "1 (max severity 8.0)"));
        html.Should().Contain("<summary>Show 1 item</summary>");
        html.Should().Contain("<li>GHSA-aaaa-bbbb-cccc</li>");

        html.Should().MatchRegex(Evidence("Transitive vulnerabilities", "1"));
        html.Should().Contain("<li>Vulnerable.Dependency 2.0.0 (GHSA-dddd-eeee-ffff)</li>");

        html.Should().MatchRegex(Evidence("Stale transitive dependencies", "1"));
        html.Should().Contain("<li>Stale.Package 1.2.3 (last release 2020-01-01)</li>");

        html.Should().MatchRegex(Evidence("Potentially abandoned transitive dependencies", "1"));
        html.Should().Contain("<li>Abandoned.Package 1.2.3 (known vulnerabilities)</li>");

        html.Should().MatchRegex(Evidence("Deprecated transitive dependencies", "1"));
        html.Should().Contain("<li>Deprecated.Package 3.0.0</li>");

        html.Should().MatchRegex(Evidence("Unmaintained critical transitives", "1"));
        html.Should().Contain("<li>Critical.Package 4.5.6 (max severity 9.0)</li>");

        // The detail panels must be collapsed by default (no "open" attribute on <details>).
        html.Should().NotContain("<details class=\"detail-toggle\" open>");
        html.Should().NotContain("<details open class=\"detail-toggle\">");
    }

    [TestMethod]
    public async Task Should_show_all_evidence_detail_list_items_in_a_collapsed_by_default_panel()
    {
        string[] staleDetails = Enumerable.Range(1, 10)
            .Select(i => $"Package{i} 1.0.0 (last release 2020-01-01)")
            .ToArray();

        var package = new PackageInfo
        {
            Name = "Contoso.Truncation",
            Version = "1.0.0",
            License = "MIT",
            StaleTransitiveDependencyCount = 10,
            StaleTransitiveDependencyDetails = staleDetails
        };

        RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [package]);

        string html = await File.ReadAllTextAsync(reportPaths.HtmlPath);

        // All 10 items should be present, without any truncation.
        foreach (string detail in staleDetails)
        {
            html.Should().Contain($"<li>{detail}</li>");
        }

        html.Should().NotContain("and 2 more");
        html.Should().Contain("<summary>Show 10 items</summary>");

        // Collapsed by default: the <details> element must not have an "open" attribute.
        html.Should().NotContain("<details class=\"detail-toggle\" open>");
        html.Should().NotContain("<details open class=\"detail-toggle\">");
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
        html.Should().MatchRegex(Evidence("License", "Missing or undetermined"));
        html.Should().MatchRegex(Evidence("License", "Present, but not one of PackageGuard&#39;s well-known license IDs: Contoso-Internal-License"));
    }

    [TestMethod]
    public async Task Should_mark_packages_with_incomplete_GitHub_data_as_inconclusive()
    {
        var incompletePackage = new PackageInfo
        {
            Name = "Contoso.RateLimited",
            Version = "1.0.0",
            Source = "nuget",
            RiskScore = 45,
            HasIncompleteGitHubRiskData = true,
            RiskDimensions = new RiskDimensions()
        };

        var completePackage = new PackageInfo
        {
            Name = "Contoso.Complete",
            Version = "1.0.0",
            Source = "nuget",
            RiskScore = 45,
            RiskDimensions = new RiskDimensions()
        };

        RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [incompletePackage, completePackage]);

        string html = await File.ReadAllTextAsync(reportPaths.HtmlPath);
        html.Should().Contain("<h2>Inconclusive results</h2>");
        html.Should().Contain("repositories of 1 package,");
        html.Should().Contain("Some GitHub repository data could not be retrieved. The signals that are missing");

        string sarif = await File.ReadAllTextAsync(reportPaths.SarifPath);
        using JsonDocument document = JsonDocument.Parse(sarif);
        JsonElement[] results = document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray().ToArray();

        JsonElement incompleteResult = results.Single(result =>
            result.GetProperty("properties").GetProperty("packageName").GetString() == "Contoso.RateLimited");
        incompleteResult.GetProperty("properties").GetProperty("inconclusive").GetBoolean().Should().BeTrue();
        incompleteResult.GetProperty("message").GetProperty("text").GetString().Should().EndWith(
            "Inconclusive: some GitHub repository data could not be retrieved.");

        JsonElement completeResult = results.Single(result =>
            result.GetProperty("properties").GetProperty("packageName").GetString() == "Contoso.Complete");
        completeResult.GetProperty("properties").TryGetProperty("inconclusive", out _).Should().BeFalse();
    }

    [TestMethod]
    public async Task Should_write_generated_report_names_inside_explicit_directory()
    {
        string explicitDirectory = Path.Combine(reportDirectory, "custom-output");

        RiskReportPaths reportPaths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [
                new PackageInfo
                {
                    Name = "Contoso.Security",
                    Version = "2.4.0",
                    RiskDimensions = new RiskDimensions()
                }
            ],
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
            [
                new PackageInfo
                {
                    Name = "Contoso.Security",
                    Version = "2.4.0",
                    RiskDimensions = new RiskDimensions()
                }
            ],
            explicitHtmlPath);

        reportPaths.HtmlPath.Should().Be(explicitHtmlPath);
        reportPaths.SarifPath.Should().Be(explicitSarifPath);
        (await File.ReadAllTextAsync(explicitHtmlPath)).Should().NotBe("old html");
        (await File.ReadAllTextAsync(explicitSarifPath)).Should().NotBe("old sarif");
    }

    [TestMethod]
    public async Task Should_list_dependency_findings_without_changing_the_risk_summary()
    {
        DependencyFinding[] findings =
        [
            new() { Kind = DependencyFindingKind.RedundantViaProject, Project = "Api", TargetFramework = "net9.0",
                PackageId = "Serilog", ResolvedVersion = "3.1.1", SafeToRemove = true, Providers = ["Domain"] },
            new() { Kind = DependencyFindingKind.VersionConflict, PackageId = "Newtonsoft.Json",
                Description = "Newtonsoft.Json resolves to multiple versions: 13.0.3 (Api); 12.0.1 (Domain)." }
        ];

        string withFindings = await WriteHtml(findings);
        string withoutFindings = await WriteHtml([]);

        withFindings.Should().Contain("Dependency Hygiene");
        withFindings.Should().Contain("Serilog 3.1.1").And.Contain("Domain").And.Contain("Keeps the resolved version");
        withFindings.Should().Contain("Newtonsoft.Json resolves to multiple versions");
        withoutFindings.Should().NotContain("Dependency Hygiene");
        ExtractSummary(withFindings).Should().Be(ExtractSummary(withoutFindings));
    }

    [TestMethod]
    public async Task Should_encode_dependency_findings_in_the_html_report()
    {
        string html = await WriteHtml([new DependencyFinding { Kind = DependencyFindingKind.RedundantViaPackage,
            Project = "<script>", PackageId = "A&B", Providers = ["C"] }]);

        html.Should().Contain("&lt;script&gt;").And.Contain("A&amp;B");
        html.Should().NotContain("<td><script>");
    }

    [TestMethod]
    public async Task Should_count_vulnerabilities_in_the_header_and_summary_table_without_showing_zero()
    {
        RiskReportPaths paths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [
                new PackageInfo { Name = "Vulnerable.One", Version = "1.0.0", RiskScore = 20, VulnerabilityCount = 2, RiskDimensions = new RiskDimensions() },
                new PackageInfo { Name = "Vulnerable.Two", Version = "1.0.0", RiskScore = 10, VulnerabilityCount = 3, RiskDimensions = new RiskDimensions() },
                new PackageInfo { Name = "Clean.Package", Version = "1.0.0", RiskScore = 5, RiskDimensions = new RiskDimensions() }
            ],
            Path.Combine(reportDirectory, Guid.NewGuid().ToString("N") + ".html"));

        string html = await File.ReadAllTextAsync(paths.HtmlPath);

        html.Should().Contain("<p class=\"status-value\"><span class=\"score-pill vuln-pill\">5</span></p><p class=\"meta\">Total</p>");
        html.Should().Contain("<th>Vulnerabilities</th>");
        html.Should().Contain("<td><span class=\"score-pill vuln-pill\">2</span></td>");
        html.Should().Contain("<td><span class=\"score-pill vuln-pill\">3</span></td>");
        html.Should().Contain("<td></td>");
        html.Should().NotContain("<td><span class=\"score-pill vuln-pill\">0</span></td>");

        // Vulnerable packages come first, with the red row style, even when their risk score is lower.
        html.IndexOf("Vulnerable.Two", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("Clean.Package", StringComparison.Ordinal));
        html.Should().MatchRegex("<tr class=\"summary-high\">\\s*<td><a href=\"#package-vulnerable-one");
    }

    [TestMethod]
    public async Task Should_not_repeat_the_header_counts_or_show_the_static_html_note()
    {
        string html = await WriteHtml([]);

        html.Should().NotContain("Status Check Summary");
        html.Should().NotContain("safe to publish as a build artifact");
    }

    [TestMethod]
    public async Task Should_link_to_the_main_sections_and_show_vulnerabilities_and_risks_in_package_details()
    {
        RiskReportPaths paths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [new PackageInfo { Name = "Vulnerable.One", Version = "1.0.0", RiskScore = 20, VulnerabilityCount = 2, RiskDimensions = new RiskDimensions() }],
            Path.Combine(reportDirectory, Guid.NewGuid().ToString("N") + ".html"),
            [new DependencyFinding { Kind = DependencyFindingKind.VersionConflict, PackageId = "A", Description = "A conflicts." }]);

        string html = await File.ReadAllTextAsync(paths.HtmlPath);

        html.Should().Contain("<a href=\"#summary\">Summary</a>");
        html.Should().Contain("<a href=\"#dependency-hygiene\">Dependency Hygiene</a>");
        html.Should().Contain("<a href=\"#package-vulnerable-one-1-0-0\">Package details</a>");
        html.Should().Contain("<h3>Risks</h3>");
        html.Should().Contain("<a class=\"anchor-link\" href=\"#package-vulnerable-one-1-0-0\"");
        html.Should().Contain("<span class=\"score-pill vuln-pill\">2</span> <span class=\"meta\">vulnerabilities</span>");
        (await WriteHtml([])).Should().NotContain("href=\"#dependency-hygiene\"");
    }

    private static string Evidence(string label, string value) =>
        $"<dt>{System.Text.RegularExpressions.Regex.Escape(label)}</dt>\\s*<dd>\\s*{System.Text.RegularExpressions.Regex.Escape(value)}";

    private async Task<string> WriteHtml(IReadOnlyCollection<DependencyFinding> findings)
    {
        RiskReportPaths paths = await RiskHtmlReportWriter.WriteAsync(
            Path.Combine(reportDirectory, "PackageGuard.sln"),
            [new PackageInfo { Name = "Contoso.Security", Version = "2.4.0", RiskScore = 10, RiskDimensions = new RiskDimensions() }],
            Path.Combine(reportDirectory, Guid.NewGuid().ToString("N") + ".html"),
            findings);

        return await File.ReadAllTextAsync(paths.HtmlPath);
    }

    /// <summary>
    /// The part of the report that carries the risk status, which has to be the same with or without dependency findings (the jump links excluded).
    /// </summary>
    private static string ExtractSummary(string html)
    {
        int start = html.IndexOf("<h1>", StringComparison.Ordinal);
        int end = html.IndexOf("<h2>Summary</h2>", StringComparison.Ordinal);
        string summary = System.Text.RegularExpressions.Regex.Replace(html[start..end], "Generated at [^<]*", "");
        return System.Text.RegularExpressions.Regex.Replace(summary, "<nav.*?</nav>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
    }
}
