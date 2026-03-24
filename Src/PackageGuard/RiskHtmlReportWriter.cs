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
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine($"  <title>{Encode(Path.GetFileName(projectPath))} - PackageGuard Risk Report</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: light dark; }");
        builder.AppendLine("    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #0f172a; color: #e2e8f0; }");
        builder.AppendLine("    main { max-width: 1280px; margin: 0 auto; padding: 24px; }");
        builder.AppendLine("    h1, h2, h3 { margin-top: 0; }");
        builder.AppendLine("    a { color: #93c5fd; }");
        builder.AppendLine("    .card { background: #111827; border: 1px solid #334155; border-radius: 12px; padding: 20px; margin-bottom: 20px; }");
        builder.AppendLine("    .summary-table { width: 100%; border-collapse: collapse; }");
        builder.AppendLine("    .summary-table th, .summary-table td { text-align: left; padding: 10px 12px; border-bottom: 1px solid #334155; vertical-align: top; }");
        builder.AppendLine("    .score-pill { display: inline-block; padding: 4px 10px; border-radius: 999px; font-weight: 600; }");
        builder.AppendLine("    .risk-low { background: #14532d; color: #dcfce7; }");
        builder.AppendLine("    .risk-medium { background: #713f12; color: #fef3c7; }");
        builder.AppendLine("    .risk-high { background: #7f1d1d; color: #fee2e2; }");
        builder.AppendLine("    .dimension-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px; margin-bottom: 20px; }");
        builder.AppendLine("    .dimension-card { background: #0b1220; border: 1px solid #334155; border-radius: 10px; padding: 16px; }");
        builder.AppendLine("    .rationale-list, .detail-list { margin: 12px 0 0; padding-left: 20px; }");
        builder.AppendLine("    .detail-list { list-style: none; padding-left: 0; }");
        builder.AppendLine("    .detail-list li { padding: 4px 0; }");
        builder.AppendLine("    .label { color: #94a3b8; font-weight: 600; }");
        builder.AppendLine("    .meta { color: #94a3b8; }");
        builder.AppendLine("    code { font-family: Cascadia Code, Consolas, monospace; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main>");
        builder.AppendLine("  <section class=\"card\">");
        builder.AppendLine("    <h1>PackageGuard Risk Report</h1>");
        builder.AppendLine($"    <p><span class=\"label\">Project:</span> <code>{Encode(projectPath)}</code></p>");
        builder.AppendLine($"    <p class=\"meta\">Generated at {Encode(generatedAt)}</p>");
        builder.AppendLine("  </section>");
        builder.AppendLine("  <section class=\"card\">");
        builder.AppendLine("    <h2>Package Summary</h2>");
        builder.AppendLine("    <table class=\"summary-table\">");
        builder.AppendLine("      <thead><tr><th>Package</th><th>Version</th><th>Overall risk</th><th>Legal</th><th>Security</th><th>Operational</th></tr></thead>");
        builder.AppendLine("      <tbody>");

        foreach (PackageInfo package in packages)
        {
            builder.AppendLine("        <tr>");
            builder.AppendLine($"          <td>{Encode(package.Name)}</td>");
            builder.AppendLine($"          <td>{Encode(package.Version)}</td>");
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
            builder.AppendLine("  <section class=\"card\">");
            builder.AppendLine($"    <h2>{Encode(package.Name)} <span class=\"meta\">{Encode(package.Version)}</span></h2>");
            builder.AppendLine($"    <p>{BuildOverallScorePill(package.RiskScore)}</p>");
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

        if (package.HasValidLicenseUrl is not null)
        {
            yield return ("License URL", package.HasValidLicenseUrl == true ? "Valid" : "Missing or invalid");
        }

        if (package.IsPackageSigned is not null)
        {
            yield return ("Package signature", package.IsPackageSigned == true ? "Signed" : "Unsigned");
        }

        if (package.HasTrustedPackageSignature is not null)
        {
            yield return ("Signature trust", package.HasTrustedPackageSignature == true ? "Verified" : "Unverified");
        }

        if (package.VulnerabilityCount > 0)
        {
            yield return ("Vulnerabilities", $"{package.VulnerabilityCount} (max severity {FormatDecimal(package.MaxVulnerabilitySeverity)})");
        }

        if (package.DependencyDepth > 0)
        {
            yield return ("Dependency depth", package.DependencyDepth.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(package.LatestStableVersion))
        {
            yield return ("Latest stable version", package.LatestStableVersion);
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

        if (package.OpenBugIssueCount is int openBugIssueCount)
        {
            yield return ("Open bug issues", openBugIssueCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.TopContributorShare is double topContributorShare)
        {
            yield return ("Top contributor share", topContributorShare.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.RecentFailedWorkflowCount is int recentFailedWorkflowCount)
        {
            yield return ("Recent failed workflows", recentFailedWorkflowCount.ToString(CultureInfo.InvariantCulture));
        }

        if (package.OpenSsfScore is double openSsfScore)
        {
            yield return ("OpenSSF Scorecard", FormatDecimal(openSsfScore));
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

        return $"<span class=\"score-pill {cssClass}\">{Encode(FormatDecimal(score))}/100 ({Encode(zone)})</span>";
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
