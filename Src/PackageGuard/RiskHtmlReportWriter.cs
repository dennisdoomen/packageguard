using System.Globalization;
using System.Net;
using System.Text;
using PackageGuard.Core;

namespace PackageGuard;

internal static class RiskHtmlReportWriter
{
    public static async Task<string> WriteAsync(string projectPath, IEnumerable<PackageInfo> packages)
    {
        string reportPath = GetReportPath(projectPath);
        string html = BuildHtml(projectPath, packages.OrderByDescending(package => package.RiskScore).ToArray());
        await File.WriteAllTextAsync(reportPath, html, Encoding.UTF8);
        return reportPath;
    }

    private static string GetReportPath(string projectPath)
    {
        string reportDirectory = Path.Combine(Path.GetTempPath(), "PackageGuard", "reports");
        Directory.CreateDirectory(reportDirectory);

        string projectName = Path.GetFileNameWithoutExtension(projectPath);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = "packageguard";
        }

        string sanitizedProjectName = string.Concat(projectName.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
        string fileName = $"{sanitizedProjectName}-risk-report-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.html";
        return Path.Combine(reportDirectory, fileName);
    }

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
            AppendDimension(builder, "Legal", package.RiskDimensions.LegalRisk, package.RiskDimensions.LegalRiskRationale);
            AppendDimension(builder, "Security", package.RiskDimensions.SecurityRisk, package.RiskDimensions.SecurityRiskRationale);
            AppendDimension(builder, "Operational", package.RiskDimensions.OperationalRisk, package.RiskDimensions.OperationalRiskRationale);
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

    private static void AppendDimension(StringBuilder builder, string label, double score, IReadOnlyCollection<string> rationale)
    {
        builder.AppendLine("      <section class=\"dimension-card\">");
        builder.AppendLine($"        <h3>{Encode(label)}</h3>");
        builder.AppendLine($"        <p>{BuildDimensionScorePill(score)}</p>");
        builder.AppendLine("        <ul class=\"rationale-list\">");

        foreach (string reason in rationale)
        {
            builder.AppendLine($"          <li>{Encode(reason)}</li>");
        }

        builder.AppendLine("        </ul>");
        builder.AppendLine("      </section>");
    }

    private static IEnumerable<(string Label, string Value)> BuildDetails(PackageInfo package)
    {
        yield return ("License", package.License ?? "Unknown");

        if (package.HasValidLicenseUrl is bool hasValidLicenseUrl)
        {
            yield return ("License URL", hasValidLicenseUrl ? "Valid" : "Missing or invalid");
        }

        if (package.IsPackageSigned is bool isPackageSigned)
        {
            yield return ("Package signature", isPackageSigned ? "Signed" : "Unsigned");
        }

        if (package.HasTrustedPackageSignature is bool hasTrustedPackageSignature)
        {
            yield return ("Signature trust", hasTrustedPackageSignature ? "Verified" : "Unverified");
        }

        if (package.HasVerifiedPublisher is bool hasVerifiedPublisher)
        {
            yield return ("Verified publisher", hasVerifiedPublisher ? "Detected" : "Not detected");
        }

        if (package.HasVerifiedReleaseSignature is bool hasVerifiedReleaseSignature)
        {
            yield return ("Verified release signature", hasVerifiedReleaseSignature ? "Detected" : "Not detected");
        }

        if (package.VerifiedCommitRatio is double verifiedCommitRatio)
        {
            yield return ("Verified commit coverage", verifiedCommitRatio.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.VulnerabilityCount > 0)
        {
            yield return ("Vulnerabilities", $"{package.VulnerabilityCount} (max severity {FormatDecimal(package.MaxVulnerabilitySeverity)})");
        }

        if (package.MedianVulnerabilityFixDays is double vulnerabilityFixDays)
        {
            yield return ("Median vulnerability fix time", $"{FormatDecimal(vulnerabilityFixDays)} days");
        }

        if (package.DependencyDepth > 0)
        {
            yield return ("Dependency depth", package.DependencyDepth.ToString(CultureInfo.InvariantCulture));
        }

        if (package.TransitiveVulnerabilityCount > 0)
        {
            yield return ("Transitive vulnerabilities", package.TransitiveVulnerabilityCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.StaleTransitiveDependencyCount is int staleTransitiveDependencyCount)
        {
            yield return ("Stale transitive dependencies", staleTransitiveDependencyCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.AbandonedTransitiveDependencyCount is int abandonedTransitiveDependencyCount)
        {
            yield return ("Potentially abandoned transitive dependencies", abandonedTransitiveDependencyCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.DeprecatedTransitiveDependencyCount is int deprecatedDependencyCount)
        {
            yield return ("Deprecated transitive dependencies", deprecatedDependencyCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.UnmaintainedCriticalTransitiveDependencyCount is int criticalDependencyCount)
        {
            yield return ("Unmaintained critical transitives", criticalDependencyCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.HasNativeBinaryAssets is bool hasNativeBinaryAssets)
        {
            yield return ("Native/binary assets", hasNativeBinaryAssets ? "Detected" : "Not detected");
        }

        if (package.IsDeprecated is bool isDeprecated)
        {
            yield return ("Deprecated package version", isDeprecated ? "Yes" : "No");
        }

        if (!string.IsNullOrWhiteSpace(package.LatestStableVersion))
        {
            yield return ("Latest stable version", package.LatestStableVersion);
        }

        if (package.LatestStablePublishedAt is DateTimeOffset latestStablePublishedAt)
        {
            yield return ("Latest stable published", latestStablePublishedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.VersionUpdateLagDays is double versionUpdateLagDays)
        {
            yield return ("Version update lag", $"{FormatDecimal(versionUpdateLagDays)} days");
        }

        if (!string.IsNullOrWhiteSpace(package.RepositoryUrl))
        {
            yield return ("Repository", package.RepositoryUrl);
        }

        if (package.PublishedAt is DateTimeOffset publishedAt)
        {
            yield return ("Published", publishedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.DownloadCount is long downloadCount)
        {
            yield return ("Downloads", downloadCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ContributorCount is int contributorCount)
        {
            yield return ("Contributors", contributorCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.RecentMaintainerCount is int recentMaintainerCount)
        {
            yield return ("Active maintainers (6 months)", recentMaintainerCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.MedianMaintainerActivityDays is double medianMaintainerActivityDays)
        {
            yield return ("Median maintainer inactivity", $"{FormatDecimal(medianMaintainerActivityDays)} days");
        }

        if (package.OpenBugIssueCount is int openBugIssueCount)
        {
            yield return ("Open bug issues", openBugIssueCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ClosedBugIssueCountLast90Days is int closedBugIssueCount)
        {
            yield return ("Closed bug issues (90 days)", closedBugIssueCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ReopenedBugIssueCountLast90Days is int reopenedBugIssueCount)
        {
            yield return ("Reopened bug issues (90 days)", reopenedBugIssueCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.MedianIssueResponseDays is double medianIssueResponseDays)
        {
            yield return ("Median issue response", $"{FormatDecimal(medianIssueResponseDays)} days");
        }

        if (package.MedianCriticalIssueResponseDays is double medianCriticalIssueResponseDays)
        {
            yield return ("Median critical issue response", $"{FormatDecimal(medianCriticalIssueResponseDays)} days");
        }

        if (package.IssueResponseCoverage is double responseCoverage)
        {
            yield return ("Issue response coverage", responseCoverage.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.IssueTriageWithinSevenDaysRate is double triageRate)
        {
            yield return ("Issue triage within 7 days", triageRate.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.MedianOpenBugAgeDays is double medianOpenBugAgeDays)
        {
            yield return ("Median open bug age", $"{FormatDecimal(medianOpenBugAgeDays)} days");
        }

        if (package.TopContributorShare is double topContributorShare)
        {
            yield return ("Top contributor share", topContributorShare.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.RecentFailedWorkflowCount is int recentFailedWorkflowCount)
        {
            yield return ("Recent failed workflows", recentFailedWorkflowCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.WorkflowFailureRate is double workflowFailureRate)
        {
            yield return ("Workflow failure rate", workflowFailureRate.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.HasFlakyWorkflowPattern != null)
        {
            yield return ("Flaky workflow pattern", package.HasFlakyWorkflowPattern == true ? "Detected" : "Not detected");
        }

        if (package.RequiredStatusCheckCount is int requiredStatusCheckCount)
        {
            yield return ("Required status checks", requiredStatusCheckCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.WorkflowPlatformCount is int workflowPlatformCount)
        {
            yield return ("Workflow platform count", workflowPlatformCount.ToString(CultureInfo.InvariantCulture));
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

        if (package.OpenSsfScore is double openSsfScore)
        {
            yield return ("OpenSSF Scorecard", FormatDecimal(openSsfScore));
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

        if (package.ReadmeUpdatedAt is DateTimeOffset readmeUpdatedAt)
        {
            yield return ("README updated", readmeUpdatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.ChangelogUpdatedAt is DateTimeOffset changelogUpdatedAt)
        {
            yield return ("CHANGELOG updated", changelogUpdatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.PrereleaseRatio is double prereleaseRatio)
        {
            yield return ("Prerelease ratio", prereleaseRatio.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.MeanReleaseIntervalDays is double meanReleaseIntervalDays)
        {
            yield return ("Mean release interval", $"{FormatDecimal(meanReleaseIntervalDays)} days");
        }

        if (package.HasReleaseNotes != null)
        {
            yield return ("Release notes signal", package.HasReleaseNotes == true ? "Detected" : "Not detected");
        }

        if (package.HasSemVerReleaseTags != null)
        {
            yield return ("Semantic version release tags", package.HasSemVerReleaseTags == true ? "Consistent" : "Inconsistent");
        }

        if (package.MajorReleaseRatio is double majorReleaseRatio)
        {
            yield return ("Major release ratio", majorReleaseRatio.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.RapidReleaseCorrectionCount is int rapidReleaseCorrectionCount)
        {
            yield return ("Rapid release corrections", rapidReleaseCorrectionCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ExternalContributionRate is double externalContributionRate)
        {
            yield return ("External contribution rate", externalContributionRate.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.UniqueReviewerCount is int uniqueReviewerCount)
        {
            yield return ("Unique reviewers", uniqueReviewerCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ReviewerDiversityRatio is double reviewerDiversityRatio)
        {
            yield return ("Reviewer diversity ratio", reviewerDiversityRatio.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.SupportedTargetFrameworks.Length > 0)
        {
            yield return ("Target frameworks", string.Join(", ", package.SupportedTargetFrameworks));
        }
    }

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

    private static string CreatePackageAnchor(PackageInfo package)
    {
        string raw = $"{package.Name}-{package.Version}".ToLowerInvariant();
        char[] anchor = raw
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        return $"package-{new string(anchor).Trim('-')}";
    }

    private static string GetRiskZone(double score)
    {
        return score switch
        {
            >= 60 => "High",
            >= 30 => "Medium",
            _ => "Low"
        };
    }

    private static string FormatDecimal(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
