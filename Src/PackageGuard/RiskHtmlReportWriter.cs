using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PackageGuard.Core;

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
        string? reportOutputPath = null)
    {
        PackageInfo[] orderedPackages = packages.OrderByDescending(package => package.RiskScore).ToArray();
        RiskReportPaths reportPaths = GetReportPaths(projectPath, reportOutputPath);
        string html = BuildHtml(projectPath, orderedPackages);
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

        string reportDirectory = Path.Combine(Path.GetTempPath(), "PackageGuard", "reports");

        Directory.CreateDirectory(reportDirectory);

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
            Directory.CreateDirectory(reportLocation);
            return CreateGeneratedReportPaths(projectPath, reportLocation);
        }

        string fullPath = Path.GetFullPath(reportLocation);
        string? parentDirectory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        string extension = Path.GetExtension(fullPath);
        string fileStem = string.IsNullOrWhiteSpace(extension)
            ? fullPath
            : Path.Combine(Path.GetDirectoryName(fullPath) ?? string.Empty, Path.GetFileNameWithoutExtension(fullPath));

        string htmlPath = extension.Equals(".sarif", StringComparison.OrdinalIgnoreCase)
            ? $"{fileStem}.html"
            : string.IsNullOrWhiteSpace(extension) || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                ? $"{fileStem}.html"
                : fullPath;

        string sarifPath = extension.Equals(".sarif", StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : $"{fileStem}.sarif";

        return new RiskReportPaths(htmlPath, sarifPath);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="reportLocation"/> refers to a directory (existing or implied
    /// by a trailing separator or lack of extension).
    /// </summary>
    private static bool LooksLikeDirectoryPath(string reportLocation)
    {
        if (Directory.Exists(reportLocation))
        {
            return true;
        }

        return reportLocation.EndsWith(Path.DirectorySeparatorChar) ||
               reportLocation.EndsWith(Path.AltDirectorySeparatorChar) ||
               string.IsNullOrWhiteSpace(Path.GetExtension(reportLocation));
    }

    /// <summary>
    /// Creates timestamped HTML and SARIF report file paths inside <paramref name="reportDirectory"/>,
    /// using a sanitised form of the project name as the file stem.
    /// </summary>
    private static RiskReportPaths CreateGeneratedReportPaths(string projectPath, string reportDirectory)
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
            Path.Combine(reportDirectory, $"{fileNamePrefix}.html"),
            Path.Combine(reportDirectory, $"{fileNamePrefix}.sarif"));
    }

    /// <summary>
    /// Builds the complete self-contained HTML string for the risk report, including styles, summary table,
    /// and per-package detail sections.
    /// </summary>
    private static string BuildHtml(string projectPath, PackageInfo[] packages)
    {
        var builder = new StringBuilder();
        string generatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        int lowRiskCount = packages.Count(package => GetRiskZone(package.RiskScore) == "Low");
        int mediumRiskCount = packages.Count(package => GetRiskZone(package.RiskScore) == "Medium");
        int highRiskCount = packages.Count(package => GetRiskZone(package.RiskScore) == "High");
        string overallZone = packages.Select(package => package.RiskScore).DefaultIfEmpty(0).Max() switch
        {
            >= 60 => "High",
            >= 30 => "Medium",
            _ => "Low"
        };

        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine($"  <title>{Encode(Path.GetFileName(projectPath))} - PackageGuard Risk Report</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #f8fafc; color: #0f172a; }");
        builder.AppendLine("    main { max-width: 1280px; margin: 0 auto; padding: 24px; }");
        builder.AppendLine("    h1, h2, h3 { margin-top: 0; }");
        builder.AppendLine("    a { color: #1d4ed8; }");
        builder.AppendLine("    .card { background: #ffffff; border: 1px solid #cbd5e1; border-radius: 12px; padding: 20px; margin-bottom: 20px; box-shadow: 0 1px 3px rgba(15, 23, 42, 0.08); }");
        builder.AppendLine("    .summary-table { width: 100%; border-collapse: collapse; }");
        builder.AppendLine("    .summary-table th, .summary-table td { text-align: left; padding: 10px 12px; border-bottom: 1px solid #e2e8f0; vertical-align: top; }");
        builder.AppendLine("    .summary-table tr.summary-low { background: #f0fdf4; }");
        builder.AppendLine("    .summary-table tr.summary-medium { background: #fffbeb; }");
        builder.AppendLine("    .summary-table tr.summary-high { background: #fef2f2; }");
        builder.AppendLine("    .score-pill { display: inline-block; padding: 4px 10px; border-radius: 999px; font-weight: 600; }");
        builder.AppendLine("    .risk-low { background: #dcfce7; color: #166534; border: 1px solid #86efac; }");
        builder.AppendLine("    .risk-medium { background: #fef3c7; color: #92400e; border: 1px solid #fcd34d; }");
        builder.AppendLine("    .risk-high { background: #fee2e2; color: #991b1b; border: 1px solid #fca5a5; }");
        builder.AppendLine("    .status-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; margin-top: 16px; }");
        builder.AppendLine("    .status-card { border: 1px solid #cbd5e1; border-radius: 10px; padding: 14px; background: #f8fafc; }");
        builder.AppendLine("    .status-value { font-size: 1.4rem; font-weight: 700; margin: 0 0 6px; }");
        builder.AppendLine("    .dimension-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px; margin-bottom: 20px; }");
        builder.AppendLine("    .dimension-card { background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 10px; padding: 16px; }");
        builder.AppendLine("    .rationale-list, .detail-list { margin: 12px 0 0; padding-left: 20px; }");
        builder.AppendLine("    .detail-list { list-style: none; padding-left: 0; }");
        builder.AppendLine("    .detail-list li { padding: 4px 0; }");
        builder.AppendLine("    .label { color: #475569; font-weight: 600; }");
        builder.AppendLine("    .meta { color: #64748b; }");
        builder.AppendLine("    code { font-family: Cascadia Code, Consolas, monospace; }");
        builder.AppendLine("    @media print { body { background: #ffffff; } main { padding: 0; } .card { box-shadow: none; break-inside: avoid; } a { color: inherit; text-decoration: none; } }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main>");
        builder.AppendLine("  <section class=\"card\" id=\"top\">");
        builder.AppendLine("    <h1>PackageGuard Risk Report</h1>");
        builder.AppendLine($"    <p><span class=\"label\">Project:</span> <code>{Encode(projectPath)}</code></p>");
        builder.AppendLine($"    <p class=\"meta\">Generated at {Encode(generatedAt)}</p>");
        builder.AppendLine("    <p class=\"meta\">Static, self-contained HTML. No scripts, no external assets, and safe to publish as a build artifact.</p>");
        builder.AppendLine("    <div class=\"status-grid\">");
        builder.AppendLine($"      <section class=\"status-card\"><p class=\"status-value\">{packages.Length}</p><p class=\"meta\">Packages analyzed</p></section>");
        builder.AppendLine($"      <section class=\"status-card\"><p class=\"status-value\">{BuildZonePill(overallZone)}</p><p class=\"meta\">Overall report status</p></section>");
        builder.AppendLine($"      <section class=\"status-card\"><p class=\"status-value\">{BuildZonePill("Low", lowRiskCount.ToString(CultureInfo.InvariantCulture))}</p><p class=\"meta\">Low-risk packages</p></section>");
        builder.AppendLine($"      <section class=\"status-card\"><p class=\"status-value\">{BuildZonePill("Medium", mediumRiskCount.ToString(CultureInfo.InvariantCulture))}</p><p class=\"meta\">Medium-risk packages</p></section>");
        builder.AppendLine($"      <section class=\"status-card\"><p class=\"status-value\">{BuildZonePill("High", highRiskCount.ToString(CultureInfo.InvariantCulture))}</p><p class=\"meta\">High-risk packages</p></section>");
        builder.AppendLine("    </div>");
        builder.AppendLine("  </section>");
        builder.AppendLine("  <section class=\"card\">");
        builder.AppendLine("    <h2>Status Check Summary</h2>");
        builder.AppendLine("    <p class=\"meta\">This section is intended to be readable as the first screen in Azure DevOps artifacts or from a linked GitHub status check.</p>");
        builder.AppendLine("    <table class=\"summary-table\">");
        builder.AppendLine("      <thead><tr><th>Severity</th><th>Count</th><th>Meaning</th></tr></thead>");
        builder.AppendLine("      <tbody>");
        builder.AppendLine($"        <tr class=\"summary-low\"><td>{BuildZonePill("Low")}</td><td>{lowRiskCount.ToString(CultureInfo.InvariantCulture)}</td><td>Package score below 30</td></tr>");
        builder.AppendLine($"        <tr class=\"summary-medium\"><td>{BuildZonePill("Medium")}</td><td>{mediumRiskCount.ToString(CultureInfo.InvariantCulture)}</td><td>Package score from 30 to 59.9</td></tr>");
        builder.AppendLine($"        <tr class=\"summary-high\"><td>{BuildZonePill("High")}</td><td>{highRiskCount.ToString(CultureInfo.InvariantCulture)}</td><td>Package score 60 or above</td></tr>");
        builder.AppendLine("      </tbody>");
        builder.AppendLine("    </table>");
        builder.AppendLine("  </section>");
        builder.AppendLine("  <section class=\"card\">");
        builder.AppendLine("    <h2>Package Summary</h2>");
        builder.AppendLine("    <table class=\"summary-table\">");
        builder.AppendLine("      <thead><tr><th>Package</th><th>Version</th><th>Overall risk</th><th>Legal</th><th>Security</th><th>Operational</th></tr></thead>");
        builder.AppendLine("      <tbody>");

        foreach (PackageInfo package in packages)
        {
            string packageAnchor = CreatePackageAnchor(package);
            string rowClass = GetRiskZone(package.RiskScore) switch
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
            builder.AppendLine("        </tr>");
        }

        builder.AppendLine("      </tbody>");
        builder.AppendLine("    </table>");
        builder.AppendLine("  </section>");

        foreach (PackageInfo package in packages)
        {
            string packageAnchor = CreatePackageAnchor(package);
            builder.AppendLine($"  <section class=\"card\" id=\"{packageAnchor}\">");
            builder.AppendLine($"    <h2>{Encode(package.Name)} <span class=\"meta\">{Encode(package.Version)}</span></h2>");
            builder.AppendLine($"    <p>{BuildOverallScorePill(package.RiskScore)}</p>");
            builder.AppendLine("    <p><a href=\"#top\">Back to report summary</a></p>");
            builder.AppendLine("    <div class=\"dimension-grid\">");
            AppendDimension(builder, package, "Legal", package.RiskDimensions.LegalRisk, package.RiskDimensions.LegalRiskRationale);
            AppendDimension(builder, package, "Security", package.RiskDimensions.SecurityRisk, package.RiskDimensions.SecurityRiskRationale);
            AppendDimension(builder, package, "Operational", package.RiskDimensions.OperationalRisk, package.RiskDimensions.OperationalRiskRationale);
            builder.AppendLine("    </div>");
            builder.AppendLine("    <h3>Evidence</h3>");
            builder.AppendLine("    <ul class=\"detail-list\">");

            foreach ((string label, string value) in BuildDetails(package))
            {
                builder.AppendLine($"      <li><span class=\"label\">{Encode(label)}:</span> {Encode(value)}</li>");
            }

            builder.AppendLine("    </ul>");
            builder.AppendLine("  </section>");
        }

        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
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
        builder.AppendLine("      <section class=\"dimension-card\">");
        builder.AppendLine($"        <h3>{Encode(label)}</h3>");
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
    /// Yields label/value pairs representing the key metadata fields shown in the package detail section.
    /// </summary>
    private static IEnumerable<(string Label, string Value)> BuildDetails(PackageInfo package)
    {
        string[] displayProjectPaths = GetDisplayProjectPaths(package);
        if (displayProjectPaths.Length > 0)
        {
            yield return ("Used by", string.Join(", ", displayProjectPaths));
        }

        yield return ("License", FormatLicenseDisplay(package));

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
            yield return ("Verified commit coverage", package.VerifiedCommitRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.VulnerabilityCount > 0)
        {
            yield return ("Vulnerabilities", $"{package.VulnerabilityCount} (max severity {FormatDecimal(package.MaxVulnerabilitySeverity)})");
        }

        if (package.MedianVulnerabilityFixDays != null)
        {
            yield return ("Median vulnerability fix time", $"{FormatDecimal(package.MedianVulnerabilityFixDays.Value)} days");
        }

        if (package.DependencyDepth > 0)
        {
            yield return ("Dependency depth", package.DependencyDepth.ToString(CultureInfo.InvariantCulture));
        }

        if (package.TransitiveVulnerabilityCount > 0)
        {
            yield return ("Transitive vulnerabilities", package.TransitiveVulnerabilityCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.StaleTransitiveDependencyCount != null)
        {
            yield return ("Stale transitive dependencies", package.StaleTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.AbandonedTransitiveDependencyCount != null)
        {
            yield return ("Potentially abandoned transitive dependencies", package.AbandonedTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.DeprecatedTransitiveDependencyCount != null)
        {
            yield return ("Deprecated transitive dependencies", package.DeprecatedTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.UnmaintainedCriticalTransitiveDependencyCount != null)
        {
            yield return ("Unmaintained critical transitives", package.UnmaintainedCriticalTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.HasNativeBinaryAssets != null)
        {
            yield return ("Native/binary assets", package.HasNativeBinaryAssets.Value ? "Detected" : "Not detected");
        }

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
            yield return ("Latest stable published", package.LatestStablePublishedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
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

        if (package.ContributorCount != null)
        {
            yield return ("Contributors", package.ContributorCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.RecentMaintainerCount != null)
        {
            yield return ("Active maintainers (6 months)", package.RecentMaintainerCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.MedianMaintainerActivityDays != null)
        {
            yield return ("Median maintainer inactivity", $"{FormatDecimal(package.MedianMaintainerActivityDays.Value)} days");
        }

        if (package.OpenBugIssueCount != null)
        {
            yield return ("Open bug issues", package.OpenBugIssueCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ClosedBugIssueCountLast90Days != null)
        {
            yield return ("Closed bug issues (90 days)", package.ClosedBugIssueCountLast90Days.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ReopenedBugIssueCountLast90Days != null)
        {
            yield return ("Reopened bug issues (90 days)", package.ReopenedBugIssueCountLast90Days.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.MedianIssueResponseDays != null)
        {
            yield return ("Median issue response", $"{FormatDecimal(package.MedianIssueResponseDays.Value)} days");
        }

        if (package.MedianCriticalIssueResponseDays != null)
        {
            yield return ("Median critical issue response", $"{FormatDecimal(package.MedianCriticalIssueResponseDays.Value)} days");
        }

        if (package.IssueResponseCoverage != null)
        {
            yield return ("Issue response coverage", package.IssueResponseCoverage.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.IssueTriageWithinSevenDaysRate != null)
        {
            yield return ("Issue triage within 7 days", package.IssueTriageWithinSevenDaysRate.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.MedianOpenBugAgeDays != null)
        {
            yield return ("Median open bug age", $"{FormatDecimal(package.MedianOpenBugAgeDays.Value)} days");
        }

        if (package.TopContributorShare != null)
        {
            yield return ("Top contributor share", package.TopContributorShare.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.RecentFailedWorkflowCount != null)
        {
            yield return ("Recent failed workflows", package.RecentFailedWorkflowCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.WorkflowFailureRate != null)
        {
            yield return ("Workflow failure rate", package.WorkflowFailureRate.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.HasFlakyWorkflowPattern != null)
        {
            yield return ("Flaky workflow pattern", package.HasFlakyWorkflowPattern == true ? "Detected" : "Not detected");
        }

        if (package.RequiredStatusCheckCount != null)
        {
            yield return ("Required status checks", package.RequiredStatusCheckCount.Value.ToString(CultureInfo.InvariantCulture));
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
            yield return ("Dependency update automation", package.HasDependencyUpdateAutomation == true ? "Detected" : "Not detected");
        }

        if (package.HasTestSignal != null)
        {
            yield return ("Test execution signal", package.HasTestSignal == true ? "Detected" : "Not detected");
        }

        if (package.HasBranchProtection != null)
        {
            yield return ("Branch protection", package.HasBranchProtection == true ? "Detected" : "Not detected");
        }

        if (package.HasProvenanceAttestation != null)
        {
            yield return ("Provenance/attestation", package.HasProvenanceAttestation == true ? "Detected" : "Not detected");
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
            yield return ("Coordinated disclosure guidance", package.HasCoordinatedDisclosure == true ? "Detected" : "Not detected");
        }

        if (package.ReadmeUpdatedAt != null)
        {
            yield return ("README updated", package.ReadmeUpdatedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.ChangelogUpdatedAt != null)
        {
            yield return ("CHANGELOG updated", package.ChangelogUpdatedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
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
            yield return ("Major-version jump ratio", package.MajorReleaseRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.RapidReleaseCorrectionCount != null)
        {
            yield return ("Rapid release corrections", package.RapidReleaseCorrectionCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ExternalContributionRate != null)
        {
            yield return ("External contribution rate", package.ExternalContributionRate.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.UniqueReviewerCount != null)
        {
            yield return ("Unique reviewers", package.UniqueReviewerCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ReviewerDiversityRatio != null)
        {
            yield return ("Reviewer diversity ratio", package.ReviewerDiversityRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.SupportedTargetFrameworks.Length > 0)
        {
            yield return ("Target frameworks", string.Join(", ", package.SupportedTargetFrameworks));
        }
    }

    /// <summary>
    /// Returns an HTML snippet for a rationale string, wrapping it in a hyperlink when the rationale
    /// matches a known linkable signal (license file, repository, README, contributing guide, changelog, or OpenSSF scorecard).
    /// </summary>
    private static string BuildRationaleContent(PackageInfo package, string rationale)
    {
        if (rationale.StartsWith("Valid license URL", StringComparison.Ordinal))
        {
            string? licenseFileUrl = TryGetLicenseFileUrl(package);
            if (licenseFileUrl is not null)
            {
                return $"<a href=\"{Encode(licenseFileUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{Encode(rationale)}</a>";
            }
        }

        if (rationale.StartsWith("Public repository available", StringComparison.Ordinal))
        {
            string? repositoryUrl = TryGetRepositoryUrl(package);
            if (repositoryUrl is not null)
            {
                return $"<a href=\"{Encode(repositoryUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{Encode(rationale)}</a>";
            }
        }

        if (rationale.StartsWith("README looks present and non-default", StringComparison.Ordinal))
        {
            string? readmeUrl = TryGetReadmeUrl(package);
            if (readmeUrl is not null)
            {
                return $"<a href=\"{Encode(readmeUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{Encode(rationale)}</a>";
            }
        }

        if (rationale.StartsWith("CONTRIBUTING guide is present", StringComparison.Ordinal))
        {
            string? contributingGuideUrl = TryGetContributingGuideUrl(package);
            if (contributingGuideUrl is not null)
            {
                return $"<a href=\"{Encode(contributingGuideUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{Encode(rationale)}</a>";
            }
        }

        if (rationale.StartsWith("CHANGELOG or release notes are present", StringComparison.Ordinal))
        {
            string? releaseHistoryUrl = TryGetReleaseHistoryUrl(package);
            if (releaseHistoryUrl is not null)
            {
                return $"<a href=\"{Encode(releaseHistoryUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{Encode(rationale)}</a>";
            }
        }

        if (rationale.StartsWith("OpenSSF Scorecard score is low", StringComparison.Ordinal))
        {
            string? scorecardUrl = TryGetOpenSsfScorecardUrl(package);
            if (scorecardUrl is not null)
            {
                return $"<a href=\"{Encode(scorecardUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">{Encode(rationale)}</a>";
            }
        }

        return Encode(rationale);
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

        string repositoryIdentifier = repositoryRoot.Replace("https://github.com/", "github.com/", StringComparison.OrdinalIgnoreCase);
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

        return $"<span class=\"score-pill {cssClass}\">{Encode(FormatDecimal(score))}/100</span>";
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

        return $"<span class=\"score-pill {cssClass}\">{Encode(FormatDecimal(score))}/10</span>";
    }

    /// <summary>
    /// Builds a colored zone pill HTML element showing a risk zone label and an optional value.
    /// </summary>
    private static string BuildZonePill(string zone, string? value = null)
    {
        string cssClass = zone switch
        {
            "High" => "risk-high",
            "Medium" => "risk-medium",
            _ => "risk-low"
        };

        string text = value is null ? zone : $"{zone}: {value}";
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

        return Path.Combine(projectPath, "package.json");
    }
}
