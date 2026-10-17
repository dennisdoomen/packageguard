using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;
using PackageGuard.Core.Risk.Scoring;
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

        AppendHtmlHead(builder, projectPath);
        AppendMaliciousPackagesSection(builder, packages);
        AppendReportHeader(builder, new ReportHeaderContext(
            projectPath, generatedAt, overallZone, packages.Length, lowRiskCount, mediumRiskCount, highRiskCount, vulnerabilityCount,
            packages.Length > 0 ? CreatePackageAnchor(packages[0]) : null, dependencyFindings.Count > 0,
            criticalVulnerabilityCount, hasVulnerabilityDetails));
        AppendVulnerabilitiesSection(builder, packages);
        AppendPackageSummaryTable(builder, packages);
        AppendDependencyHygieneSection(builder, dependencyFindings);
        AppendPackageDetailSections(builder, packages);

        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendHtmlHead(StringBuilder builder, string projectPath)
    {
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine($"  <title>{Encode(ChainablePath.From(projectPath).Name)} - PackageGuard Risk Report</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine(
            "    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #f8fafc; color: #0f172a; }");
        builder.AppendLine("    main { max-width: 1920px; margin: 0 auto; padding: 24px; }");
        builder.AppendLine("    h1, h2, h3 { margin-top: 0; }");
        builder.AppendLine("    a { color: #1d4ed8; }");
        builder.AppendLine(
            "    .card { background: #ffffff; border: 1px solid #cbd5e1; border-radius: 12px; padding: 20px; margin-bottom: 20px; box-shadow: 0 1px 3px rgba(15, 23, 42, 0.08); }");
        builder.AppendLine("    .summary-table { width: 100%; border-collapse: collapse; }");
        builder.AppendLine(
            "    .summary-table th, .summary-table td { text-align: left; padding: 10px 12px; border-bottom: 1px solid #e2e8f0; vertical-align: top; }");
        builder.AppendLine("    .summary-table tr.summary-low { background: #f0fdf4; }");
        builder.AppendLine("    .summary-table tr.summary-medium { background: #fffbeb; }");
        builder.AppendLine("    .summary-table tr.summary-high { background: #fef2f2; }");
        builder.AppendLine(
            "    .score-pill { display: inline-block; padding: 4px 10px; border-radius: 999px; font-weight: 600; }");
        builder.AppendLine("    .risk-low { background: #dcfce7; color: #166534; border: 1px solid #86efac; }");
        builder.AppendLine("    .risk-medium { background: #fef3c7; color: #92400e; border: 1px solid #fcd34d; }");
        builder.AppendLine("    .risk-high { background: #fee2e2; color: #991b1b; border: 1px solid #fca5a5; }");
        builder.AppendLine(
            "    .vuln-pill { background: #f3e8ff; color: #6b21a8; border: 1px solid #d8b4fe; }");
        builder.AppendLine(
            "    .status-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; margin-top: 16px; }");
        builder.AppendLine(
            "    .status-card { border: 1px solid #cbd5e1; border-radius: 10px; padding: 14px; background: #f8fafc; }");
        builder.AppendLine("    .status-value { font-size: 1.4rem; font-weight: 700; margin: 0 0 6px; }");
        builder.AppendLine(
            "    .dimension-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px; margin-bottom: 20px; }");
        builder.AppendLine(
            "    .anchor-link { margin-left: 10px; text-decoration: none; color: #94a3b8; opacity: 0; transition: opacity 0.15s; }");
        builder.AppendLine("    h2:hover .anchor-link, .anchor-link:focus { opacity: 1; }");
        builder.AppendLine("    .jump-links { display: flex; flex-wrap: wrap; gap: 6px 16px; margin: 12px 0 0; }");
        builder.AppendLine("    .card table + h3 { margin-top: 24px; }");
        builder.AppendLine("    .rationale-list { margin: 12px 0 0; padding-left: 20px; }");
        builder.AppendLine(
            "    .evidence-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(380px, 1fr)); gap: 16px; }");
        builder.AppendLine(
            "    .evidence-card { background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 10px; padding: 16px; }");
        builder.AppendLine(
            "    .evidence-card h4 { margin: 0 0 10px; font-size: 0.8rem; letter-spacing: 0.05em; text-transform: uppercase; color: #64748b; }");
        builder.AppendLine(
            "    .evidence-list { display: grid; grid-template-columns: minmax(140px, 35%) 1fr; gap: 6px 16px; margin: 0; }");
        builder.AppendLine("    .evidence-list dt { color: #475569; font-weight: 600; }");
        builder.AppendLine("    .evidence-list dd { margin: 0; overflow-wrap: anywhere; }");
        builder.AppendLine("    .detail-toggle { display: inline; margin-left: 6px; }");
        builder.AppendLine("    .detail-toggle summary { display: inline; cursor: pointer; color: #1d4ed8; }");
        builder.AppendLine(
            "    .detail-sublist { list-style: disc; margin: 8px 0 0; padding-left: 20px; color: #334155; }");
        builder.AppendLine("    .detail-sublist li { padding: 2px 0; }");
        builder.AppendLine("    .label { color: #475569; font-weight: 600; }");
        builder.AppendLine("    .rationale-list strong { color: #991b1b; }");
        builder.AppendLine("    .meta { color: #64748b; }");
        builder.AppendLine(
            "    .malicious-card { border: 3px solid #991b1b; background: #fef2f2; }");
        builder.AppendLine("    .malicious-card h2 { color: #991b1b; }");
        builder.AppendLine(
            "    .summary-blocks { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 20px; margin-bottom: 20px; }");
        builder.AppendLine("    .summary-blocks .card { margin-bottom: 0; }");
        builder.AppendLine("    .summary-blocks h2 { font-size: 0.8rem; letter-spacing: 0.05em; text-transform: uppercase; color: #64748b; }");
        builder.AppendLine("    .summary-blocks .status-grid { margin-top: 0; grid-template-columns: repeat(auto-fit, minmax(110px, 1fr)); }");
        builder.AppendLine("    .critical-pill { background: #7f1d1d; color: #ffffff; border: 1px solid #450a0a; }");
        builder.AppendLine("    code { font-family: Cascadia Code, Consolas, monospace; }");
        builder.AppendLine(
            "    @media print { body { background: #ffffff; } main { padding: 0; } .anchor-link { display: none; } .card { box-shadow: none; break-inside: avoid; } a { color: inherit; text-decoration: none; } }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main>");
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

            builder.AppendLine(
                $"        <tr class=\"{rowClass}\"><td>{Encode(GetVulnerabilityLabel(vulnerability))}</td><td><a href=\"#{CreatePackageAnchor(package)}\">{Encode(package.Name)} {Encode(package.Version)}</a></td><td>{FormatDecimal(vulnerability.Severity)}</td><td>{epss}</td><td>{BuildVulnerabilityStatus(vulnerability)}</td></tr>");
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
            builder.AppendLine($"          <td>{BuildOverallScorePill(package.RiskScore)}</td>");
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
            builder.AppendLine($"    <p>{BuildOverallScorePill(package.RiskScore)}{vulnerabilityBadge}</p>");
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

            foreach ((string title, DetailEntry[] entries) in BuildDetailGroups(package))
            {
                if (entries.Length == 0)
                {
                    continue;
                }

                builder.AppendLine("      <section class=\"evidence-card\">");
                builder.AppendLine($"        <h4>{Encode(title)}</h4>");
                builder.AppendLine("        <dl class=\"evidence-list\">");

                foreach (DetailEntry entry in entries)
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
    private static void AppendDetailListItem(StringBuilder builder, DetailEntry entry)
    {
        builder.AppendLine($"          <dt>{Encode(entry.Label)}</dt>");

        if (entry.Items is { Count: > 0 })
        {
            builder.AppendLine("          <dd>");
            builder.AppendLine($"            {Encode(entry.Value)}");
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
            builder.AppendLine($"          <dd>{Encode(entry.Value)}</dd>");
        }
    }

    /// <summary>
    /// An evidence label/value pair, optionally accompanied by the full list of items (e.g. package names) behind
    /// the value, rendered as a collapsed-by-default panel.
    /// </summary>
    private readonly record struct DetailEntry(string Label, string Value, IReadOnlyCollection<string>? Items = null)
    {
        public static implicit operator DetailEntry((string Label, string Value) pair) => new(pair.Label, pair.Value);
    }

    /// <summary>
    /// Returns the evidence of a package in titled groups, each rendered as its own card in the detail section.
    /// </summary>
    private static IEnumerable<(string Title, DetailEntry[] Entries)> BuildDetailGroups(PackageInfo package)
    {
        yield return ("Package", BuildIdentityDetails(package).ToArray());
        yield return ("Security", BuildSecurityDetails(package).ToArray());
        yield return ("Maintenance", BuildOperationalDetails(package).ToArray());
        yield return ("Releases", BuildReleaseDetails(package).ToArray());
    }

    private static IEnumerable<DetailEntry> BuildIdentityDetails(PackageInfo package)
    {
        string[] displayProjectPaths = GetDisplayProjectPaths(package);
        if (displayProjectPaths.Length > 0)
        {
            yield return ("Used by", string.Join(", ", displayProjectPaths));
        }

        yield return ("License", FormatLicenseDisplay(package));

        if (package.IsDeprecated != null)
        {
            yield return ("Deprecated package version", package.IsDeprecated.Value ? "Yes" : "No");
        }

        if (!string.IsNullOrWhiteSpace(package.LatestStableVersion))
        {
            yield return ("Latest stable version", package.LatestStableVersion);
        }

        if (package.LatestStablePublishedAt != null)
        {
            yield return ("Latest stable published",
                package.LatestStablePublishedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.VersionUpdateLagDays != null)
        {
            yield return ("Version update lag", $"{FormatDecimal(package.VersionUpdateLagDays.Value)} days");
        }

        if (package.PublishedAt != null)
        {
            yield return ("Published", package.PublishedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.DownloadCount != null)
        {
            yield return ("Downloads", package.DownloadCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.SupportedTargetFrameworks.Length > 0)
        {
            yield return ("Target frameworks", string.Join(", ", package.SupportedTargetFrameworks));
        }
    }

    private static IEnumerable<DetailEntry> BuildSecurityDetails(PackageInfo package)
    {
        if (package.IsPackageSigned != null)
        {
            yield return ("Package signature", package.IsPackageSigned.Value ? "Signed" : "Unsigned");
        }

        if (package.HasTrustedPackageSignature != null)
        {
            yield return ("Signature trust", package.HasTrustedPackageSignature.Value ? "Verified" : "Unverified");
        }

        if (package.HasVerifiedPublisher != null)
        {
            yield return ("Verified publisher", package.HasVerifiedPublisher.Value ? "Detected" : "Not detected");
        }

        if (package.HasVerifiedReleaseSignature != null)
        {
            yield return ("Verified release signature", package.HasVerifiedReleaseSignature.Value ? "Detected" : "Not detected");
        }

        if (package.VerifiedCommitRatio != null)
        {
            yield return ("Verified commit coverage",
                package.VerifiedCommitRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.VulnerabilityCount > 0)
        {
            yield return new DetailEntry(
                "Vulnerabilities",
                $"{package.VulnerabilityCount} (max severity {FormatDecimal(package.MaxVulnerabilitySeverity)})",
                package.ActiveVulnerabilities.Select(vulnerability => vulnerability.Id).ToArray());
        }

        if (package.MaliciousAdvisoryIds.Length > 0)
        {
            yield return ("Malicious package advisories", string.Join(", ", package.MaliciousAdvisoryIds));
        }

        if (package.MaxEpssScore is { } epss)
        {
            yield return ("Highest EPSS exploitation probability (30 days)",
                $"{(epss * 100).ToString("0.0##", CultureInfo.InvariantCulture)}%");
        }

        if (package.KnownExploitedCveIds is { Length: > 0 } exploitedCveIds)
        {
            yield return new DetailEntry("Known exploited (CISA KEV)", exploitedCveIds.Length.ToString(CultureInfo.InvariantCulture),
                exploitedCveIds);
        }

        if (package.MedianVulnerabilityFixDays != null)
        {
            yield return ("Median advisory publication-to-update time", $"{FormatDecimal(package.MedianVulnerabilityFixDays.Value)} days");
        }

        if (package.DependencyDepth > 0)
        {
            yield return ("Dependency depth", package.DependencyDepth.ToString(CultureInfo.InvariantCulture));
        }

        if (package.TransitiveVulnerabilityCount > 0)
        {
            yield return new DetailEntry(
                "Transitive vulnerabilities",
                package.TransitiveVulnerabilityCount.ToString(CultureInfo.InvariantCulture),
                package.VulnerableTransitiveDependencyDetails);
        }

        if (package.StaleTransitiveDependencyCount != null)
        {
            yield return new DetailEntry(
                "Stale transitive dependencies",
                package.StaleTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture),
                package.StaleTransitiveDependencyDetails);
        }

        if (package.AbandonedTransitiveDependencyCount != null)
        {
            yield return new DetailEntry(
                "Potentially abandoned transitive dependencies",
                package.AbandonedTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture),
                package.AbandonedTransitiveDependencyDetails);
        }

        if (package.DeprecatedTransitiveDependencyCount != null)
        {
            yield return new DetailEntry(
                "Deprecated transitive dependencies",
                package.DeprecatedTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture),
                package.DeprecatedTransitiveDependencyDetails);
        }

        if (package.UnmaintainedCriticalTransitiveDependencyCount != null)
        {
            yield return new DetailEntry(
                "Unmaintained critical transitives",
                package.UnmaintainedCriticalTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture),
                package.UnmaintainedCriticalTransitiveDependencyDetails);
        }

        if (package.HasNativeBinaryAssets != null)
        {
            yield return ("Native/binary assets", package.HasNativeBinaryAssets.Value ? "Detected" : "Not detected");
        }

        if (package.HasSecurityPolicy != null)
        {
            yield return ("Security policy", package.HasSecurityPolicy == true ? "Present" : "Missing");
        }

        if (package.HasDetailedSecurityPolicy != null)
        {
            yield return ("Detailed security policy", package.HasDetailedSecurityPolicy == true ? "Detected" : "Not detected");
        }

        if (package.HasCoordinatedDisclosure != null)
        {
            yield return ("Coordinated disclosure guidance",
                package.HasCoordinatedDisclosure == true ? "Detected" : "Not detected");
        }

        if (package.HasProvenanceAttestation != null)
        {
            yield return ("Provenance/attestation", package.HasProvenanceAttestation == true ? "Detected" : "Not detected");
        }
    }

    private static IEnumerable<DetailEntry> BuildOperationalDetails(PackageInfo package)
    {
        if (package.ContributorCount != null)
        {
            yield return ("Contributors", package.ContributorCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.RecentMaintainerCount != null)
        {
            yield return ("Active maintainers (6 months)",
                package.RecentMaintainerCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.MedianMaintainerActivityDays != null)
        {
            yield return ("Median maintainer inactivity", $"{FormatDecimal(package.MedianMaintainerActivityDays.Value)} days");
        }

        if (package.TopContributorShare != null)
        {
            yield return ("Top contributor share",
                package.TopContributorShare.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.OpenBugIssueCount != null)
        {
            yield return ("Open bug issues", package.OpenBugIssueCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ClosedBugIssueCountLast90Days != null)
        {
            yield return ("Closed bug issues (90 days)",
                package.ClosedBugIssueCountLast90Days.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ReopenedBugIssueCountLast90Days != null)
        {
            yield return ("Reopened bug issues (90 days)",
                package.ReopenedBugIssueCountLast90Days.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.MedianIssueResponseDays != null)
        {
            yield return ("Median issue response", $"{FormatDecimal(package.MedianIssueResponseDays.Value)} days");
        }

        if (package.MedianCriticalIssueResponseDays != null)
        {
            yield return ("Median critical issue response",
                $"{FormatDecimal(package.MedianCriticalIssueResponseDays.Value)} days");
        }

        if (package.IssueResponseCoverage != null)
        {
            yield return ("Issue response coverage",
                package.IssueResponseCoverage.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.IssueTriageWithinSevenDaysRate != null)
        {
            yield return ("Issue triage within 7 days",
                package.IssueTriageWithinSevenDaysRate.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.MedianOpenBugAgeDays != null)
        {
            yield return ("Median open bug age", $"{FormatDecimal(package.MedianOpenBugAgeDays.Value)} days");
        }

        foreach (var item in BuildWorkflowDetails(package))
        {
            yield return item;
        }
    }

    private static IEnumerable<DetailEntry> BuildWorkflowDetails(PackageInfo package)
    {
        if (package.RecentFailedWorkflowCount != null)
        {
            yield return ("Recent failed workflows",
                package.RecentFailedWorkflowCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.WorkflowFailureRate != null)
        {
            yield return ("Workflow failure rate",
                package.WorkflowFailureRate.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.HasFlakyWorkflowPattern != null)
        {
            yield return ("Flaky workflow pattern", package.HasFlakyWorkflowPattern == true ? "Detected" : "Not detected");
        }

        if (package.RequiredStatusCheckCount != null)
        {
            yield return ("Required status checks",
                package.RequiredStatusCheckCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.WorkflowPlatformCount != null)
        {
            yield return ("Workflow platform count", package.WorkflowPlatformCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.HasCoverageWorkflowSignal != null)
        {
            yield return ("Coverage workflow signal", package.HasCoverageWorkflowSignal == true ? "Detected" : "Not detected");
        }

        if (package.HasReproducibleBuildSignal != null)
        {
            yield return ("Reproducible-build signal", package.HasReproducibleBuildSignal == true ? "Detected" : "Not detected");
        }

        if (package.HasDependencyUpdateAutomation != null)
        {
            yield return ("Dependency update automation",
                package.HasDependencyUpdateAutomation == true ? "Detected" : "Not detected");
        }

        if (package.HasTestSignal != null)
        {
            yield return ("Test execution signal", package.HasTestSignal == true ? "Detected" : "Not detected");
        }

        if (package.HasBranchProtection != null)
        {
            yield return ("Branch protection", package.HasBranchProtection == true ? "Detected" : "Not detected");
        }
    }

    private static IEnumerable<DetailEntry> BuildReleaseDetails(PackageInfo package)
    {
        if (package.ReadmeUpdatedAt != null)
        {
            yield return ("README updated", package.ReadmeUpdatedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.ChangelogUpdatedAt != null)
        {
            yield return ("CHANGELOG updated",
                package.ChangelogUpdatedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.PrereleaseRatio != null)
        {
            yield return ("Prerelease ratio", package.PrereleaseRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.MeanReleaseIntervalDays != null)
        {
            yield return ("Mean release interval", $"{FormatDecimal(package.MeanReleaseIntervalDays.Value)} days");
        }

        if (package.HasReleaseNotes != null)
        {
            yield return ("Release notes signal", package.HasReleaseNotes == true ? "Detected" : "Not detected");
        }

        if (package.HasSemVerReleaseTags != null)
        {
            yield return ("Semantic version release tags", package.HasSemVerReleaseTags == true ? "Consistent" : "Inconsistent");
        }

        if (package.MajorReleaseRatio != null)
        {
            yield return ("Major-version jump ratio",
                package.MajorReleaseRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.RapidReleaseCorrectionCount != null)
        {
            yield return ("Rapid release corrections",
                package.RapidReleaseCorrectionCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ExternalContributionRate != null)
        {
            yield return ("External contribution rate",
                package.ExternalContributionRate.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.UniqueReviewerCount != null)
        {
            yield return ("Unique reviewers", package.UniqueReviewerCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ReviewerDiversityRatio != null)
        {
            yield return ("Reviewer diversity ratio",
                package.ReviewerDiversityRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Returns an HTML snippet for a rationale string, wrapping it in a hyperlink when the rationale
    /// matches a known linkable signal (license file, repository, README, contributing guide, changelog, or OpenSSF scorecard).
    /// </summary>
    private static string BuildRationaleContent(PackageInfo package, string rationale)
    {
        if (rationale.StartsWith("License URL", StringComparison.Ordinal))
        {
            string? licenseFileUrl = TryGetLicenseFileUrl(package);
            if (licenseFileUrl is not null)
            {
                return
                    $"<a href=\"{Encode(licenseFileUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{FormatRationaleText(rationale)}</a>";
            }
        }

        if (rationale.StartsWith("Public repository available", StringComparison.Ordinal))
        {
            string? repositoryUrl = TryGetRepositoryUrl(package);
            if (repositoryUrl is not null)
            {
                return
                    $"<a href=\"{Encode(repositoryUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{FormatRationaleText(rationale)}</a>";
            }
        }

        if (rationale.StartsWith("README looks present and non-default", StringComparison.Ordinal))
        {
            string? readmeUrl = TryGetReadmeUrl(package);
            if (readmeUrl is not null)
            {
                return
                    $"<a href=\"{Encode(readmeUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{FormatRationaleText(rationale)}</a>";
            }
        }

        if (rationale.StartsWith("CONTRIBUTING guide is present", StringComparison.Ordinal))
        {
            string? contributingGuideUrl = TryGetContributingGuideUrl(package);
            if (contributingGuideUrl is not null)
            {
                return
                    $"<a href=\"{Encode(contributingGuideUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{FormatRationaleText(rationale)}</a>";
            }
        }

        if (rationale.StartsWith("CHANGELOG or release notes are present", StringComparison.Ordinal))
        {
            string? releaseHistoryUrl = TryGetReleaseHistoryUrl(package);
            if (releaseHistoryUrl is not null)
            {
                return
                    $"<a href=\"{Encode(releaseHistoryUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{FormatRationaleText(rationale)}</a>";
            }
        }

        if (rationale.StartsWith("OpenSSF Scorecard score is low", StringComparison.Ordinal))
        {
            string? scorecardUrl = TryGetOpenSsfScorecardUrl(package);
            if (scorecardUrl is not null)
            {
                return
                    $"<a href=\"{Encode(scorecardUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{FormatRationaleText(rationale)}</a>";
            }
        }

        return FormatRationaleText(rationale);
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
    /// Returns the validated repository URL for the package, or <c>null</c> if none is available or valid.
    /// </summary>
    private static string? TryGetRepositoryUrl(PackageInfo package)
    {
        if (Uri.TryCreate(package.RepositoryUrl, UriKind.Absolute, out Uri? repositoryUri))
        {
            return repositoryUri.ToString();
        }

        return null;
    }

    /// <summary>
    /// Returns a URL pointing to the license file on GitHub, or falls back to the package's license URL,
    /// or <c>null</c> if neither is resolvable.
    /// </summary>
    private static string? TryGetLicenseFileUrl(PackageInfo package)
    {
        string? repositoryRoot = TryGetGitHubRepositoryRoot(package.RepositoryUrl);
        if (repositoryRoot is not null)
        {
            return $"{repositoryRoot}/blob/HEAD/LICENSE";
        }

        if (Uri.TryCreate(package.LicenseUrl, UriKind.Absolute, out Uri? licenseUri))
        {
            return licenseUri.ToString();
        }

        return null;
    }

    /// <summary>
    /// Returns the root <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;</c> URL derived from
    /// <paramref name="repositoryUrl"/>, or <c>null</c> if the URL is not a recognised GitHub URL.
    /// </summary>
    private static string? TryGetGitHubRepositoryRoot(string? repositoryUrl)
    {
        string? identifier = TryGetGitHubIdentifier(repositoryUrl);
        return identifier is null ? null : $"https://github.com/{identifier}";
    }

    /// <summary>
    /// Returns a URL pointing to the README anchor on the GitHub repository, or <c>null</c> if the repository
    /// URL is not a recognised GitHub URL.
    /// </summary>
    private static string? TryGetReadmeUrl(PackageInfo package)
    {
        string? repositoryRoot = TryGetGitHubRepositoryRoot(package.RepositoryUrl);
        return repositoryRoot is null ? null : $"{repositoryRoot}#readme";
    }

    /// <summary>
    /// Returns a URL pointing to the CONTRIBUTING.md file on the GitHub repository, or <c>null</c> if the
    /// repository URL is not a recognised GitHub URL.
    /// </summary>
    private static string? TryGetContributingGuideUrl(PackageInfo package)
    {
        string? repositoryRoot = TryGetGitHubRepositoryRoot(package.RepositoryUrl);
        return repositoryRoot is null ? null : $"{repositoryRoot}/blob/HEAD/CONTRIBUTING.md";
    }

    /// <summary>
    /// Returns a URL pointing to the release notes or changelog for the package on GitHub,
    /// preferring the Releases page over a CHANGELOG.md link, or <c>null</c> if neither is available.
    /// </summary>
    private static string? TryGetReleaseHistoryUrl(PackageInfo package)
    {
        string? repositoryRoot = TryGetGitHubRepositoryRoot(package.RepositoryUrl);
        if (repositoryRoot is null)
        {
            return null;
        }

        if (package.HasReleaseNotes == true)
        {
            return $"{repositoryRoot}/releases";
        }

        if (package.HasChangelog == true && package.HasDefaultChangelog != true)
        {
            return $"{repositoryRoot}/blob/HEAD/CHANGELOG.md";
        }

        return null;
    }

    /// <summary>
    /// Returns a URL to the OpenSSF Scorecard viewer for the package's GitHub repository, or <c>null</c>
    /// if the repository URL is not a recognised GitHub URL.
    /// </summary>
    private static string? TryGetOpenSsfScorecardUrl(PackageInfo package)
    {
        string? repositoryRoot = TryGetGitHubRepositoryRoot(package.RepositoryUrl);
        if (repositoryRoot is null)
        {
            return null;
        }

        string repositoryIdentifier =
            repositoryRoot.Replace("https://github.com/", "github.com/", StringComparison.OrdinalIgnoreCase);

        return $"https://securityscorecards.dev/viewer/?uri={repositoryIdentifier}";
    }

    /// <summary>
    /// Extracts the <c>&lt;owner&gt;/&lt;repo&gt;</c> identifier from a GitHub repository URL,
    /// or returns <c>null</c> if the URL is not a valid GitHub URL.
    /// </summary>
    private static string? TryGetGitHubIdentifier(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return null;
        }

        Match match = Regex.Match(
            repositoryUrl,
            @"github\.com/(?<owner>[a-zA-Z0-9._-]+)/(?<repo>[a-zA-Z0-9._-]+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        string repositoryName = match.Groups["repo"].Value.TrimEnd('.');
        repositoryName = repositoryName.Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase);
        return $"{match.Groups["owner"].Value}/{repositoryName}";
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
    /// Formats a decimal value to one decimal place using the invariant culture.
    /// </summary>
    private static string FormatDecimal(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a total risk score (overall or per-dimension) to one decimal place, dropping the decimal
    /// entirely when the value is a round number so e.g. "7.0/10" reads as "7/10".
    /// </summary>
    private static string FormatTotalScore(double value)
    {
        string formatted = FormatDecimal(value);
        return formatted.EndsWith(".0", StringComparison.Ordinal) ? formatted[..^2] : formatted;
    }

    /// <summary>
    /// Returns a human-readable license string for display, noting when the license is missing,
    /// undetermined, or not one of PackageGuard's recognised SPDX identifiers.
    /// </summary>
    private static string FormatLicenseDisplay(PackageInfo package)
    {
        if (string.IsNullOrWhiteSpace(package.License) ||
            package.License.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "Missing or undetermined";
        }

        if (!IsWellKnownLicense(package.License))
        {
            return $"Present, but not one of PackageGuard's well-known license IDs: {package.License}";
        }

        return package.License;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="license"/> contains a substring matching one of PackageGuard's
    /// well-known SPDX license family markers.
    /// </summary>
    private static bool IsWellKnownLicense(string license)
    {
        string[] knownLicenseMarkers =
        [
            "MIT", "Apache", "BSD", "ISC", "Unlicense", "WTFPL", "CC0",
            "LGPL", "MPL",
            "GPL", "AGPL", "SSPL", "Commons Clause", "BUSL", "BCL"
        ];

        return knownLicenseMarkers.Any(marker =>
            license.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// HTML-encodes <paramref name="value"/> to prevent injection in generated markup.
    /// </summary>
    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    /// <summary>
    /// Returns a deduplicated, sorted array of display-friendly project paths referencing the given package.
    /// </summary>
    private static string[] GetDisplayProjectPaths(PackageInfo package)
    {
        return package.Projects
            .Select(path => ToDisplayProjectPath(package, path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Normalises a raw project path for display, appending <c>package.json</c> for npm packages
    /// that reference a directory rather than an explicit file.
    /// </summary>
    private static string ToDisplayProjectPath(PackageInfo package, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return string.Empty;
        }

        if (!package.Source.Equals("npm", StringComparison.OrdinalIgnoreCase))
        {
            return projectPath;
        }

        if (projectPath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
        {
            return projectPath;
        }

        return ChainablePath.From(projectPath) / "package.json";
    }
}
