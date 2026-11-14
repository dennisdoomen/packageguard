using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;
using PackageGuard.Core.Risk.Scoring;
using PackageGuard.Reports;
using Pathy;

namespace PackageGuard;

/// <summary>
/// Generates HTML and SARIF risk reports from package analysis results and writes them to disk.
/// </summary>
internal static class RiskHtmlReportWriter
{
    /// <summary>
    /// Environment variable that overrides the output directory for generated risk reports.
    /// </summary>
    internal const string ReportDirectoryEnvironmentVariable = "PACKAGEGUARD_REPORT_DIRECTORY";

    /// <summary>
    /// UTF-8 encoding without a byte-order mark, used when writing report files to disk.
    /// </summary>
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    /// <summary>
    /// Matches a <c>{{name}}</c> placeholder in the embedded page template.
    /// </summary>
    private static readonly Regex TemplatePlaceholderPattern = new(@"\{\{(?<name>\w+)\}\}", RegexOptions.Compiled);

    /// <summary>
    /// The HTML skeleton of the report, with placeholders for the title, the styles and the content.
    /// </summary>
    private static readonly string PageTemplate = ReadEmbeddedResource("risk-report.html");

    /// <summary>
    /// The style sheet that is embedded in every report, so the report stays a single self-contained file.
    /// </summary>
    private static readonly string StyleSheet = ReadEmbeddedResource("risk-report.css").TrimEnd();

    /// <summary>
    /// Writes HTML and SARIF risk reports for <paramref name="packages"/> to the resolved output path and
    /// returns the resulting file paths.
    /// </summary>
    public static async Task<RiskReportPaths> WriteAsync(
        string projectPath,
        IEnumerable<PackageInfo> packages,
        string? reportOutputPath = null,
        IReadOnlyCollection<DependencyFinding>? dependencyFindings = null)
    {
        PackageInfo[] orderedPackages = packages.OrderByDescending(package => package.RiskScore).ToArray();
        RiskReportPaths reportPaths = GetReportPaths(projectPath, reportOutputPath);
        string html = BuildHtml(projectPath, orderedPackages, dependencyFindings ?? []);
        string sarif = RiskSarifReportWriter.BuildSarif(projectPath, orderedPackages);

        await File.WriteAllTextAsync(reportPaths.HtmlPath, html, Utf8WithoutBom);
        await File.WriteAllTextAsync(reportPaths.SarifPath, sarif, Utf8WithoutBom);

        return reportPaths;
    }

    /// <summary>
    /// Resolves output file paths for the HTML and SARIF reports, falling back to a generated temp-directory
    /// path when no explicit output location is configured.
    /// </summary>
    private static RiskReportPaths GetReportPaths(string projectPath, string? configuredReportPath = null)
    {
        string? reportLocation = string.IsNullOrWhiteSpace(configuredReportPath)
            ? Environment.GetEnvironmentVariable(ReportDirectoryEnvironmentVariable)
            : configuredReportPath;

        if (!string.IsNullOrWhiteSpace(reportLocation))
        {
            return ResolveConfiguredReportPaths(projectPath, reportLocation);
        }

        ChainablePath reportDirectory = ChainablePath.Temp / "PackageGuard" / "reports";

        reportDirectory.CreateDirectoryRecursively();

        return CreateGeneratedReportPaths(projectPath, reportDirectory);
    }

    /// <summary>
    /// Resolves report paths from a user-supplied location, treating it as a directory when it ends with a
    /// separator or has no extension, and as an explicit file path otherwise.
    /// </summary>
    private static RiskReportPaths ResolveConfiguredReportPaths(string projectPath, string reportLocation)
    {
        if (LooksLikeDirectoryPath(reportLocation))
        {
            ChainablePath directory = ChainablePath.From(reportLocation);
            directory.CreateDirectoryRecursively();
            return CreateGeneratedReportPaths(projectPath, directory);
        }

        ChainablePath fullPath = ChainablePath.From(reportLocation).ToAbsolute();
        ChainablePath parentDirectory = fullPath.Directory;
        if (parentDirectory != ChainablePath.Empty)
        {
            parentDirectory.CreateDirectoryRecursively();
        }

        string extension = fullPath.Extension;
        string fileStem = string.IsNullOrWhiteSpace(extension)
            ? fullPath
            : parentDirectory / Path.GetFileNameWithoutExtension(fullPath.Name);

        string fullPathAsString = fullPath;
        string htmlPath = extension.Equals(".sarif", StringComparison.OrdinalIgnoreCase)
            ? $"{fileStem}.html"
            : string.IsNullOrWhiteSpace(extension) || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                ? $"{fileStem}.html"
                : fullPathAsString;

        string sarifPath = extension.Equals(".sarif", StringComparison.OrdinalIgnoreCase)
            ? fullPathAsString
            : $"{fileStem}.sarif";

        return new RiskReportPaths(htmlPath, sarifPath);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="reportLocation"/> refers to a directory (existing or implied
    /// by a trailing separator or lack of extension).
    /// </summary>
    private static bool LooksLikeDirectoryPath(string reportLocation)
    {
        ChainablePath path = ChainablePath.From(reportLocation);
        if (path.IsDirectory)
        {
            return true;
        }

        return reportLocation.EndsWith(Path.DirectorySeparatorChar) ||
               reportLocation.EndsWith(Path.AltDirectorySeparatorChar) ||
               string.IsNullOrWhiteSpace(path.Extension);
    }

    /// <summary>
    /// Creates timestamped HTML and SARIF report file paths inside <paramref name="reportDirectory"/>,
    /// using a sanitised form of the project name as the file stem.
    /// </summary>
    private static RiskReportPaths CreateGeneratedReportPaths(string projectPath, ChainablePath reportDirectory)
    {
        string projectName = Path.GetFileNameWithoutExtension(projectPath);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = "packageguard";
        }

        string sanitizedProjectName = string.Concat(projectName.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));

        string fileNamePrefix = $"{sanitizedProjectName}-risk-report-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        return new RiskReportPaths(
            reportDirectory / $"{fileNamePrefix}.html",
            reportDirectory / $"{fileNamePrefix}.sarif");
    }

    /// <summary>
    /// Builds the complete self-contained HTML string for the risk report, including styles, summary table,
    /// and per-package detail sections.
    /// </summary>
    private static string BuildHtml(string projectPath, PackageInfo[] packages, IReadOnlyCollection<DependencyFinding> dependencyFindings)
    {
        var builder = new StringBuilder();
        string generatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        int lowRiskCount = packages.Count(package => GetRiskZone(package.RiskScore) == "Low");
        int mediumRiskCount = packages.Count(package => GetRiskZone(package.RiskScore) == "Medium");
        int highRiskCount = packages.Count(package => GetRiskZone(package.RiskScore) == "High");
        int vulnerabilityCount = packages.Sum(package => package.VulnerabilityCount);
        int criticalVulnerabilityCount = packages.Sum(package => package.ActiveVulnerabilities.Count(vulnerability => vulnerability.IsKnownExploited));
        bool hasVulnerabilityDetails = packages.Any(package => package.Vulnerabilities.Length > 0);
        string overallZone = packages.Select(package => package.RiskScore).DefaultIfEmpty(0).Max() switch
        {
            >= 60 => "High",
            >= 30 => "Medium",
            _ => "Low"
        };

        AppendMaliciousPackagesSection(builder, packages);
        AppendReportHeader(builder, new ReportHeaderContext(
            projectPath, generatedAt, overallZone, packages.Length, lowRiskCount, mediumRiskCount, highRiskCount, vulnerabilityCount,
            packages.Length > 0 ? CreatePackageAnchor(packages[0]) : null, dependencyFindings.Count > 0,
            criticalVulnerabilityCount, hasVulnerabilityDetails));
        AppendInconclusiveNotice(builder, packages);
        AppendVulnerabilitiesSection(builder, packages);
        AppendPackageSummaryTable(builder, packages);
        AppendDependencyHygieneSection(builder, dependencyFindings);
        AppendPackageDetailSections(builder, packages);

        return RenderPage(projectPath, builder.ToString().TrimEnd());
    }

    /// <summary>
    /// Fills the embedded page template with the title, the embedded style sheet and the generated
    /// <paramref name="content"/>.
    /// </summary>
    private static string RenderPage(string projectPath, string content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = Encode(ChainablePath.From(projectPath).Name),
            ["styles"] = StyleSheet,
            ["content"] = content
        };

        return TemplatePlaceholderPattern.Replace(PageTemplate, match => values[match.Groups["name"].Value]);
    }

    /// <summary>
    /// Reads an embedded resource from the <c>Reports</c> folder, using the line endings of the current platform.
    /// </summary>
    private static string ReadEmbeddedResource(string fileName)
    {
        using Stream stream = typeof(RiskHtmlReportWriter).Assembly.GetManifestResourceStream($"PackageGuard.Reports.{fileName}")
            ?? throw new InvalidOperationException($"The embedded resource {fileName} is missing");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings();
    }

    private sealed record ReportHeaderContext(
        string ProjectPath,
        string GeneratedAt,
        string OverallZone,
        int PackageCount,
        int LowRiskCount,
        int MediumRiskCount,
        int HighRiskCount,
        int VulnerabilityCount,
        string? FirstPackageAnchor,
        bool HasDependencyFindings,
        int CriticalVulnerabilityCount,
        bool HasVulnerabilityDetails);

    /// <summary>
    /// Adds a prominent section above everything else that lists the packages with a malicious-package advisory.
    /// </summary>
    private static void AppendMaliciousPackagesSection(StringBuilder builder, PackageInfo[] packages)
    {
        PackageInfo[] maliciousPackages = packages.Where(package => package.MaliciousAdvisoryIds.Length > 0).ToArray();
        if (maliciousPackages.Length == 0)
        {
            return;
        }

        builder.AppendLine("  <section class=\"card malicious-card\" id=\"malicious\">");
        builder.AppendLine("    <h2>Known malicious packages</h2>");
        builder.AppendLine(
            "    <p><strong>Remove these packages now.</strong> Machines that installed them may be compromised. This fails the run regardless of the risk score.</p>");

        builder.AppendLine("    <ul>");
        foreach (PackageInfo package in maliciousPackages)
        {
            string advisories = string.Join(", ", package.MaliciousAdvisoryIds.Select(Encode));
            builder.AppendLine(
                $"      <li><a href=\"#{CreatePackageAnchor(package)}\">{Encode(package.Name)} {Encode(package.Version)}</a> <span class=\"meta\">({advisories})</span></li>");
        }

        builder.AppendLine("    </ul>");
        builder.AppendLine("  </section>");
    }

    private static void AppendReportHeader(StringBuilder builder, ReportHeaderContext ctx)
    {
        string projectPath = ctx.ProjectPath;
        string generatedAt = ctx.GeneratedAt;
        string overallZone = ctx.OverallZone;
        int packageCount = ctx.PackageCount;
        int lowRiskCount = ctx.LowRiskCount;
        int mediumRiskCount = ctx.MediumRiskCount;
        int highRiskCount = ctx.HighRiskCount;
        int vulnerabilityCount = ctx.VulnerabilityCount;
        string? firstPackageAnchor = ctx.FirstPackageAnchor;
        bool hasDependencyFindings = ctx.HasDependencyFindings;
        builder.AppendLine("  <section class=\"card\" id=\"top\">");
        builder.AppendLine("    <h1>PackageGuard Risk Report</h1>");
        builder.AppendLine($"    <p><span class=\"label\">Project:</span> <code>{Encode(projectPath)}</code></p>");
        builder.AppendLine($"    <p class=\"meta\">Generated at {Encode(generatedAt)}</p>");
        builder.AppendLine("    <nav class=\"jump-links\">");
        builder.AppendLine("      <span class=\"label\">Jump to:</span>");
        builder.AppendLine("      <a href=\"#summary\">Summary</a>");
        if (ctx.HasVulnerabilityDetails)
        {
            builder.AppendLine("      <a href=\"#vulnerabilities\">Vulnerabilities</a>");
        }

        if (hasDependencyFindings)
        {
            builder.AppendLine("      <a href=\"#dependency-hygiene\">Dependency Hygiene</a>");
        }

        if (firstPackageAnchor is not null)
        {
            builder.AppendLine($"      <a href=\"#{firstPackageAnchor}\">Package details</a>");
        }

        builder.AppendLine("    </nav>");
        builder.AppendLine("  </section>");
        AppendSummaryBlocks(builder, ctx);
    }

    /// <summary>
    /// Adds the row of three blocks below the title: the packages and overall status, the vulnerabilities, and the
    /// number of packages per risk level.
    /// </summary>
    private static void AppendSummaryBlocks(StringBuilder builder, ReportHeaderContext ctx)
    {
        string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        builder.AppendLine("  <div class=\"summary-blocks\">");

        builder.AppendLine("    <section class=\"card\">");
        builder.AppendLine("      <h2>Packages</h2>");
        builder.AppendLine("      <div class=\"status-grid\">");
        builder.AppendLine(
            $"        <section class=\"status-card\"><p class=\"status-value\">{ctx.PackageCount}</p><p class=\"meta\">Packages analyzed</p></section>");
        builder.AppendLine(
            $"        <section class=\"status-card\"><p class=\"status-value\">{BuildZonePill(ctx.OverallZone)}</p><p class=\"meta\">Overall report status</p></section>");
        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");

        builder.AppendLine("    <section class=\"card\">");
        builder.AppendLine("      <h2>Vulnerabilities</h2>");
        builder.AppendLine("      <div class=\"status-grid\">");
        builder.AppendLine(
            $"        <section class=\"status-card\"><p class=\"status-value\"><span class=\"score-pill critical-pill\">{Count(ctx.CriticalVulnerabilityCount)}</span></p><p class=\"meta\">Critical (CISA KEV)</p></section>");
        builder.AppendLine(
            $"        <section class=\"status-card\"><p class=\"status-value\">{BuildVulnerabilityPill(ctx.VulnerabilityCount)}</p><p class=\"meta\">Total</p></section>");
        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");

        builder.AppendLine("    <section class=\"card\">");
        builder.AppendLine("      <h2>Risk levels</h2>");
        builder.AppendLine("      <div class=\"status-grid\">");
        builder.AppendLine(
            $"        <section class=\"status-card\"><p class=\"status-value\">{BuildZonePill("Low", Count(ctx.LowRiskCount), showZoneLabel: false)}</p><p class=\"meta\">Low (below 30)</p></section>");
        builder.AppendLine(
            $"        <section class=\"status-card\"><p class=\"status-value\">{BuildZonePill("Medium", Count(ctx.MediumRiskCount), showZoneLabel: false)}</p><p class=\"meta\">Medium (30 to 59.9)</p></section>");
        builder.AppendLine(
            $"        <section class=\"status-card\"><p class=\"status-value\">{BuildZonePill("High", Count(ctx.HighRiskCount), showZoneLabel: false)}</p><p class=\"meta\">High (60 or above)</p></section>");
        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");

        builder.AppendLine("  </div>");
    }

    /// <summary>
    /// Warns that the scores of some packages are inconclusive, because GitHub did not answer every request for their
    /// repositories. Their missing signals are left out of the score instead of counting as risks.
    /// </summary>
    private static void AppendInconclusiveNotice(StringBuilder builder, PackageInfo[] packages)
    {
        int count = packages.Count(package => package.HasIncompleteGitHubRiskData);
        if (count == 0)
        {
            return;
        }

        string packageCount = count == 1 ? "1 package" : $"{count.ToString(CultureInfo.InvariantCulture)} packages";

        builder.AppendLine("  <section class=\"card inconclusive-card\" id=\"inconclusive\">");
        builder.AppendLine("    <h2>Inconclusive results</h2>");
        builder.AppendLine(
            $"    <p>GitHub did not answer every request for the repositories of {packageCount}, usually because the API rate limit ran out. " +
            "The signals that could not be collected are left out of their scores, so those scores may change once the data is available. " +
            "These packages are marked <span class=\"score-pill inconclusive-pill\">Inconclusive</span>. " +
            "Run again after the rate limit resets, or pass a GitHub token through <code>--github-api-key</code> or the <code>GITHUB_API_KEY</code> environment variable.</p>");
        builder.AppendLine("  </section>");
    }

    /// <summary>
    /// Returns the marker shown next to the score of a package whose GitHub data is incomplete, or an empty string.
    /// </summary>
    private static string BuildInconclusiveMarker(PackageInfo package) =>
        package.HasIncompleteGitHubRiskData
            ? " <span class=\"score-pill inconclusive-pill\" title=\"Some GitHub repository data could not be retrieved\">Inconclusive</span>"
            : "";

    /// <summary>
    /// Lists every vulnerability in one table: the actively exploited (CISA KEV) ones in red, the other ones that still
    /// apply in yellow, and the ones that a policy exception marked as not applicable in green.
    /// </summary>
    private static void AppendVulnerabilitiesSection(StringBuilder builder, PackageInfo[] packages)
    {
        var rows = packages
            .SelectMany(package => package.Vulnerabilities.Select(vulnerability => (package, vulnerability)))
            .OrderBy(row => row.vulnerability.IsSuppressed)
            .ThenByDescending(row => row.vulnerability.IsKnownExploited)
            .ThenByDescending(row => row.vulnerability.Severity)
            .ToArray();

        if (rows.Length == 0)
        {
            return;
        }

        builder.AppendLine("  <section class=\"card\" id=\"vulnerabilities\">");
        builder.AppendLine("    <h2>Vulnerabilities</h2>");
        builder.AppendLine("    <table class=\"summary-table\">");
        builder.AppendLine(
            "      <thead><tr><th>Vulnerability</th><th>Package</th><th>Severity</th><th>EPSS</th><th>Status</th></tr></thead>");

        builder.AppendLine("      <tbody>");
        foreach ((PackageInfo package, OsvVulnerabilityRecord vulnerability) in rows)
        {
            string epss = vulnerability.EpssScore is { } score
                ? $"{(score * 100).ToString("0.0##", CultureInfo.InvariantCulture)}%"
                : "";

            string rowClass = vulnerability.IsSuppressed ? "summary-low"
                : vulnerability.IsKnownExploited ? "summary-high"
                : "summary-medium";

            string severity = RiskDisplay.FormatDecimal(vulnerability.Severity);
            string packageLink =
                $"<a href=\"#{CreatePackageAnchor(package)}\">{Encode(package.Name)} {Encode(package.Version)}</a>";

            builder.AppendLine(
                $"        <tr class=\"{rowClass}\"><td>{Encode(GetVulnerabilityLabel(vulnerability))}</td><td>{packageLink}</td>" +
                $"<td>{severity}</td><td>{epss}</td><td>{BuildVulnerabilityStatus(vulnerability)}</td></tr>");
        }

        builder.AppendLine("      </tbody>");
        builder.AppendLine("    </table>");
        builder.AppendLine("  </section>");
    }

    /// <summary>
    /// Describes whether a vulnerability applies: not applicable until a review date, critical, or expired and applicable again.
    /// </summary>
    private static string BuildVulnerabilityStatus(OsvVulnerabilityRecord vulnerability)
    {
        VulnerabilityException? exception = vulnerability.Exception;
        if (vulnerability.IsSuppressed)
        {
            return $"Not applicable until {exception!.ReviewOn:yyyy-MM-dd}: {Encode(exception.Reason)}";
        }

        string status = vulnerability.IsKnownExploited
            ? "<span class=\"score-pill critical-pill\">Critical (CISA KEV)</span>"
            : "Applicable";

        return exception is null
            ? status
            : $"{status} <span class=\"meta\">Not-applicable decision expired on {exception.ReviewOn:yyyy-MM-dd}: {Encode(exception.Reason)}</span>";
    }

    /// <summary>
    /// Shows the CVE identifier when there is one, as that is what people search for, next to the OSV identifier.
    /// </summary>
    private static string GetVulnerabilityLabel(OsvVulnerabilityRecord vulnerability) =>
        vulnerability.CveIds.FirstOrDefault() is { } cveId && !cveId.Equals(vulnerability.Id, StringComparison.OrdinalIgnoreCase)
            ? $"{cveId} ({vulnerability.Id})"
            : vulnerability.Id;

    private static void AppendPackageSummaryTable(StringBuilder builder, PackageInfo[] packages)
    {
        builder.AppendLine("  <section class=\"card\" id=\"summary\">");
        builder.AppendLine("    <h2>Summary</h2>");
        builder.AppendLine("    <table class=\"summary-table\">");
        builder.AppendLine(
            "      <thead><tr><th>Package</th><th>Version</th><th>Overall risk</th><th>Legal</th><th>Security</th><th>Operational</th><th>Vulnerabilities</th></tr></thead>");
        builder.AppendLine("      <tbody>");

        IEnumerable<PackageInfo> summaryOrder = packages
            .OrderByDescending(package => package.VulnerabilityCount > 0)
            .ThenByDescending(package => package.RiskScore);

        foreach (PackageInfo package in summaryOrder)
        {
            string packageAnchor = CreatePackageAnchor(package);
            string rowClass = package.VulnerabilityCount > 0
                ? "summary-high"
                : GetRiskZone(package.RiskScore) switch
                {
                    "High" => "summary-high",
                    "Medium" => "summary-medium",
                    _ => "summary-low"
                };

            builder.AppendLine($"        <tr class=\"{rowClass}\">");
            builder.AppendLine($"          <td><a href=\"#{packageAnchor}\">{Encode(package.Name)}</a></td>");
            builder.AppendLine($"          <td><a href=\"#{packageAnchor}\">{Encode(package.Version)}</a></td>");
            builder.AppendLine($"          <td>{BuildOverallScorePill(package.RiskScore)}{BuildInconclusiveMarker(package)}</td>");
            builder.AppendLine($"          <td>{BuildDimensionScorePill(package.RiskDimensions.LegalRisk)}</td>");
            builder.AppendLine($"          <td>{BuildDimensionScorePill(package.RiskDimensions.SecurityRisk)}</td>");
            builder.AppendLine($"          <td>{BuildDimensionScorePill(package.RiskDimensions.OperationalRisk)}</td>");
            builder.AppendLine(
                $"          <td>{(package.VulnerabilityCount > 0 ? BuildVulnerabilityPill(package.VulnerabilityCount) : "")}</td>");
            builder.AppendLine("        </tr>");
        }

        builder.AppendLine("      </tbody>");
        builder.AppendLine("    </table>");
        builder.AppendLine("  </section>");
    }

    /// <summary>
    /// Appends the dependency hygiene findings. They are reported next to the risk data, but are not scored, so they
    /// never affect the risk zones or the overall report status.
    /// </summary>
    private static void AppendDependencyHygieneSection(StringBuilder builder, IReadOnlyCollection<DependencyFinding> findings)
    {
        if (findings.Count == 0)
        {
            return;
        }

        DependencyFinding[] redundancies = findings.Where(f => f.Kind != DependencyFindingKind.VersionConflict).ToArray();
        DependencyFinding[] conflicts = findings.Where(f => f.Kind == DependencyFindingKind.VersionConflict).ToArray();

        builder.AppendLine("  <section class=\"card\" id=\"dependency-hygiene\">");
        builder.AppendLine("    <h2>Dependency Hygiene</h2>");
        builder.AppendLine(
            "    <p class=\"meta\">Based on the restore graph, not on your source code, and not part of the risk score. A package can be redundant here and still be used directly in code.</p>");

        if (redundancies.Length > 0)
        {
            builder.AppendLine($"    <h3>Redundant package references ({redundancies.Length.ToString(CultureInfo.InvariantCulture)})</h3>");
            builder.AppendLine("    <table class=\"summary-table\">");
            builder.AppendLine(
                "      <thead><tr><th>Project</th><th>Framework</th><th>Package</th><th>Already provided by</th><th>Removing it</th></tr></thead>");
            builder.AppendLine("      <tbody>");
            foreach (DependencyFinding finding in redundancies)
            {
                string effect = finding.SafeToRemove
                    ? "Keeps the resolved version"
                    : $"Lowers {finding.ResolvedVersion} to {finding.WouldResolveTo}";

                builder.AppendLine(
                    $"        <tr><td>{Encode(finding.Project)}</td><td>{Encode(finding.TargetFramework)}</td><td>{Encode(finding.PackageId)} {Encode(finding.ResolvedVersion)}</td><td>{Encode(string.Join(", ", finding.Providers))}</td><td>{Encode(effect)}</td></tr>");
            }

            builder.AppendLine("      </tbody>");
            builder.AppendLine("    </table>");
        }

        if (conflicts.Length > 0)
        {
            builder.AppendLine($"    <h3>Version conflicts ({conflicts.Length.ToString(CultureInfo.InvariantCulture)})</h3>");
            builder.AppendLine("    <table class=\"summary-table\">");
            builder.AppendLine("      <thead><tr><th>Package</th><th>Resolved versions</th></tr></thead>");
            builder.AppendLine("      <tbody>");
            foreach (DependencyFinding finding in conflicts)
            {
                builder.AppendLine(
                    $"        <tr><td>{Encode(finding.PackageId)}</td><td>{Encode(finding.Description)}</td></tr>");
            }

            builder.AppendLine("      </tbody>");
            builder.AppendLine("    </table>");
        }

        builder.AppendLine("  </section>");
    }

    private static void AppendPackageDetailSections(StringBuilder builder, PackageInfo[] packages)
    {
        foreach (PackageInfo package in packages)
        {
            string packageAnchor = CreatePackageAnchor(package);
            builder.AppendLine($"  <section class=\"card\" id=\"{packageAnchor}\">");
            builder.AppendLine(
                $"    <h2>{Encode(package.Name)} <span class=\"meta\">{Encode(package.Version)}</span><a class=\"anchor-link\" href=\"#{packageAnchor}\" aria-label=\"Link to {Encode(package.Name)} {Encode(package.Version)}\">#</a></h2>");
            string vulnerabilityBadge = package.VulnerabilityCount > 0
                ? $" {BuildVulnerabilityPill(package.VulnerabilityCount)} <span class=\"meta\">{(package.VulnerabilityCount == 1 ? "vulnerability" : "vulnerabilities")}</span>"
                : "";
            builder.AppendLine($"    <p>{BuildOverallScorePill(package.RiskScore)}{BuildInconclusiveMarker(package)}{vulnerabilityBadge}</p>");
            if (package.HasIncompleteGitHubRiskData)
            {
                builder.AppendLine(
                    "    <p class=\"meta\">Some GitHub repository data could not be retrieved. The signals that are missing are left out of the score instead of counting as risks.</p>");
            }

            builder.AppendLine("    <p><a href=\"#top\">Back to report summary</a></p>");
            builder.AppendLine("    <h3>Risks</h3>");
            builder.AppendLine("    <div class=\"dimension-grid\">");
            AppendDimension(builder, package, "Legal", package.RiskDimensions.LegalRisk,
                package.RiskDimensions.LegalRiskRationale);
            AppendDimension(builder, package, "Security", package.RiskDimensions.SecurityRisk,
                package.RiskDimensions.SecurityRiskRationale);
            AppendDimension(builder, package, "Operational", package.RiskDimensions.OperationalRisk,
                package.RiskDimensions.OperationalRiskRationale);
            builder.AppendLine("    </div>");
            builder.AppendLine("    <h3>Evidence</h3>");
            builder.AppendLine("    <div class=\"evidence-grid\">");

            foreach (EvidenceGroup group in RiskReportViewModelBuilder.BuildEvidenceGroups(package))
            {
                builder.AppendLine("      <section class=\"evidence-card\">");
                builder.AppendLine($"        <h4>{Encode(group.Title)}</h4>");
                builder.AppendLine("        <dl class=\"evidence-list\">");

                foreach (EvidenceEntry entry in group.Entries)
                {
                    AppendDetailListItem(builder, entry);
                }

                builder.AppendLine("        </dl>");
                builder.AppendLine("      </section>");
            }

            builder.AppendLine("    </div>");
            builder.AppendLine("  </section>");
        }
    }

    /// <summary>
    /// Appends an HTML risk dimension card showing the score pill and rationale list for a single risk dimension.
    /// </summary>
    private static void AppendDimension(
        StringBuilder builder,
        PackageInfo package,
        string label,
        double score,
        IReadOnlyCollection<string> rationale)
    {
        builder.AppendLine("      <section class=\"evidence-card\">");
        builder.AppendLine($"        <h4>{Encode(label)}</h4>");
        builder.AppendLine($"        <p>{BuildDimensionScorePill(score)}</p>");
        builder.AppendLine("        <ul class=\"rationale-list\">");

        foreach (string reason in rationale)
        {
            builder.AppendLine($"          <li>{BuildRationaleContent(package, reason)}</li>");
        }

        builder.AppendLine("        </ul>");
        builder.AppendLine("      </section>");
    }

    /// <summary>
    /// Appends an evidence term and description pair, rendering any accompanying items as a collapsed-by-default panel so long
    /// dependency lists don't dominate the page.
    /// </summary>
    private static void AppendDetailListItem(StringBuilder builder, EvidenceEntry entry)
    {
        builder.AppendLine($"          <dt>{Encode(entry.Label)}</dt>");
        string valueHtml = BuildValueHtml(entry);

        if (entry.Items is { Count: > 0 })
        {
            builder.AppendLine("          <dd>");
            builder.AppendLine($"            {valueHtml}");
            builder.AppendLine("            <details class=\"detail-toggle\">");
            builder.AppendLine(
                $"              <summary>Show {entry.Items.Count} {(entry.Items.Count == 1 ? "item" : "items")}</summary>");
            builder.AppendLine("              <ul class=\"detail-sublist\">");

            foreach (string item in entry.Items)
            {
                builder.AppendLine($"                <li>{Encode(item)}</li>");
            }

            builder.AppendLine("              </ul>");
            builder.AppendLine("            </details>");
            builder.AppendLine("          </dd>");
        }
        else
        {
            builder.AppendLine($"          <dd>{valueHtml}</dd>");
        }
    }

    /// <summary>
    /// Returns the encoded value of an entry, wrapped in a link that opens in a new tab when the entry has a URL.
    /// </summary>
    private static string BuildValueHtml(EvidenceEntry entry) =>
        entry.Url is null
            ? Encode(entry.Value)
            : $"<a href=\"{Encode(entry.Url)}\" target=\"_blank\" rel=\"noopener noreferrer\">{Encode(entry.Value)}</a>";

    /// <summary>
    /// Returns an HTML snippet for a rationale string, wrapping it in a hyperlink when the rationale
    /// matches a known linkable signal (license file, repository, README, contributing guide, changelog, or OpenSSF scorecard).
    /// </summary>
    private static string BuildRationaleContent(PackageInfo package, string rationale)
    {
        string text = FormatRationaleText(rationale);

        return RiskReportViewModelBuilder.TryGetRationaleUrl(package, rationale) is { } url
            ? $"<a href=\"{Encode(url)}\" target=\"_blank\" rel=\"noreferrer noopener\">{text}</a>"
            : text;
    }

    /// <summary>
    /// Matches the trailing "(+score)" contribution suffix that <see cref="RiskEvaluationHelpers.CreateRationale"/>
    /// appends to a rationale description.
    /// </summary>
    private static readonly Regex ContributionSuffixPattern = new(@"\(\+(?<value>\d+(?:\.\d+)?)\)$", RegexOptions.Compiled);

    /// <summary>
    /// HTML-encodes a rationale string, rendering its trailing "(+score)" contribution in bold when that
    /// contribution is greater than zero, so the signals that actually increased the risk score stand out.
    /// </summary>
    private static string FormatRationaleText(string rationale)
    {
        Match match = ContributionSuffixPattern.Match(rationale);
        if (!match.Success || !double.TryParse(
                match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double contribution) ||
            contribution <= 0)
        {
            return Encode(rationale);
        }

        string description = rationale[..match.Index];
        string contributionSuffix = rationale[match.Index..];
        return $"{Encode(description)}<strong>{Encode(contributionSuffix)}</strong>";
    }

    /// <summary>
    /// Builds a colored score pill HTML element showing the overall risk score out of 100.
    /// </summary>
    private static string BuildOverallScorePill(double score)
    {
        string zone = GetRiskZone(score);
        string cssClass = zone switch
        {
            "High" => "risk-high",
            "Medium" => "risk-medium",
            _ => "risk-low"
        };

        return $"<span class=\"score-pill {cssClass}\">{Encode(FormatTotalScore(score))}/100</span>";
    }

    /// <summary>
    /// Builds a colored score pill HTML element showing a dimension risk score out of 10.
    /// </summary>
    private static string BuildDimensionScorePill(double score)
    {
        double normalizedScore = score * 10;
        string zone = GetRiskZone(normalizedScore);
        string cssClass = zone switch
        {
            "High" => "risk-high",
            "Medium" => "risk-medium",
            _ => "risk-low"
        };

        return $"<span class=\"score-pill {cssClass}\">{Encode(FormatTotalScore(score))}/10</span>";
    }

    /// <summary>
    /// Builds a pill showing a vulnerability count, styled differently from the risk score pills.
    /// </summary>
    private static string BuildVulnerabilityPill(int count) =>
        $"<span class=\"score-pill vuln-pill\">{count.ToString(CultureInfo.InvariantCulture)}</span>";

    /// <summary>
    /// Builds a colored zone pill HTML element showing a risk zone label and an optional value.
    /// </summary>
    private static string BuildZonePill(string zone, string? value = null, bool showZoneLabel = true)
    {
        string cssClass = zone switch
        {
            "High" => "risk-high",
            "Medium" => "risk-medium",
            _ => "risk-low"
        };

        string text = value is null ? zone : showZoneLabel ? $"{zone}: {value}" : value;
        return $"<span class=\"score-pill {cssClass}\">{Encode(text)}</span>";
    }

    /// <summary>
    /// Creates a stable HTML anchor ID for the given package, derived from its name and version.
    /// </summary>
    private static string CreatePackageAnchor(PackageInfo package)
    {
        string raw = $"{package.Name}-{package.Version}".ToLowerInvariant();
        char[] anchor = raw
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        return $"package-{new string(anchor).Trim('-')}";
    }

    /// <summary>
    /// Returns the risk zone label ("High", "Medium", or "Low") for a given numeric score.
    /// </summary>
    private static string GetRiskZone(double score)
    {
        return score switch
        {
            >= 60 => "High",
            >= 30 => "Medium",
            _ => "Low"
        };
    }

    /// <summary>
    /// Formats a total risk score (overall or per-dimension) to one decimal place, dropping the decimal
    /// entirely when the value is a round number so e.g. "7.0/10" reads as "7/10".
    /// </summary>
    private static string FormatTotalScore(double value)
    {
        string formatted = RiskDisplay.FormatDecimal(value);
        return formatted.EndsWith(".0", StringComparison.Ordinal) ? formatted[..^2] : formatted;
    }

    /// <summary>
    /// HTML-encodes <paramref name="value"/> to prevent injection in generated markup.
    /// </summary>
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
