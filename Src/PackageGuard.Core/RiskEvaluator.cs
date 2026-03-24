using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

/// <summary>
/// Evaluates and calculates risk metrics for NuGet packages.
/// </summary>
public class RiskEvaluator(ILogger logger)
{
    private sealed record RiskDimensionEvaluation(double Score, string[] Rationale);

    /// <summary>
    /// Evaluates the risk for a given package and updates its risk properties.
    /// </summary>
    /// <param name="package">The package to evaluate.</param>
    public void EvaluateRisk(PackageInfo package)
    {
        logger.LogDebug("Evaluating risk for package {PackageName} {Version}", package.Name, package.Version);

        var legalRisk = EvaluateLegalRisk(package);
        var securityRisk = EvaluateSecurityRisk(package);
        var operationalRisk = EvaluateOperationalRisk(package);

        var riskDimensions = new RiskDimensions
        {
            LegalRisk = legalRisk.Score,
            LegalRiskRationale = legalRisk.Rationale,
            SecurityRisk = securityRisk.Score,
            SecurityRiskRationale = securityRisk.Rationale,
            OperationalRisk = operationalRisk.Score,
            OperationalRiskRationale = operationalRisk.Rationale
        };

        package.RiskDimensions = riskDimensions;
        package.RiskScore = riskDimensions.OverallRisk * 10; // Scale to 0-100

        logger.LogDebug("Risk evaluation complete for {PackageName}: Overall={OverallRisk}, Legal={LegalRisk}, Security={SecurityRisk}, Operational={OperationalRisk}",
            package.Name,
            FormatScore(package.RiskScore),
            FormatScore(riskDimensions.LegalRisk),
            FormatScore(riskDimensions.SecurityRisk),
            FormatScore(riskDimensions.OperationalRisk));
    }

    /// <summary>
    /// Evaluates legal risk based on license information and compliance factors.
    /// </summary>
    private RiskDimensionEvaluation EvaluateLegalRisk(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (string.IsNullOrEmpty(package.License) || package.License == "Unknown")
        {
            risk += 6.0;
            rationale.Add(CreateRationale("Unknown or missing license", 6.0));
        }
        else if (IsRestrictiveLicense(package.License))
        {
            risk += 6.0;
            rationale.Add(CreateRationale($"Restrictive license ({package.License})", 6.0));
        }
        else if (IsWeakCopyleftLicense(package.License))
        {
            risk += 3.0;
            rationale.Add(CreateRationale($"Weak copyleft license ({package.License})", 3.0));
        }
        else if (IsPermissiveLicense(package.License))
        {
            rationale.Add(CreateRationale($"Permissive license ({package.License})", 0.0));
        }
        else
        {
            rationale.Add(CreateRationale($"Unclassified license ({package.License})", 0.0));
        }

        if (package.HasValidLicenseUrl == false || string.IsNullOrEmpty(package.LicenseUrl))
        {
            risk += 1.0;
            rationale.Add(CreateRationale("Missing or invalid license URL", 1.0));
        }
        else if (package.HasValidLicenseUrl == true)
        {
            rationale.Add(CreateRationale("Valid license URL", 0.0));
        }

        if (package.IsLicensePolicyCompatible == false)
        {
            risk += 3.0;
            rationale.Add(CreateRationale("License is incompatible with configured policy", 3.0));
        }
        else if (package.IsLicensePolicyCompatible == true)
        {
            rationale.Add(CreateRationale("License matches configured policy", 0.0));
        }

        return CreateEvaluation(risk, rationale);
    }

    /// <summary>
    /// Evaluates security risk based on known vulnerabilities and source transparency.
    /// </summary>
    private RiskDimensionEvaluation EvaluateSecurityRisk(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (string.IsNullOrEmpty(package.RepositoryUrl))
        {
            risk += 4.0;
            rationale.Add(CreateRationale("No public repository available", 4.0));
        }
        else
        {
            rationale.Add(CreateRationale("Public repository available", 0.0));
        }

        double vulnerabilityRisk = 0;
        if (package.VulnerabilityCount > 0)
        {
            var severityContribution = Math.Min(4.0, package.MaxVulnerabilitySeverity / 2.0);
            vulnerabilityRisk += severityContribution;
            rationale.Add(CreateRationale(
                $"Known vulnerabilities found ({package.VulnerabilityCount}, max severity {FormatScore(package.MaxVulnerabilitySeverity)})",
                severityContribution));
        }
        else
        {
            rationale.Add(CreateRationale("No direct vulnerabilities reported", 0.0));
        }

        if (package.HasPatchedVulnerabilityInLast90Days)
        {
            vulnerabilityRisk += 1.0;
            rationale.Add(CreateRationale("Package has a recent vulnerability fix window (<90 days)", 1.0));
        }

        if (package.VulnerabilityCount > 0 && package.HasAvailableSecurityFix)
        {
            vulnerabilityRisk += 1.0;
            rationale.Add(CreateRationale("A security fix is available for a known vulnerability", 1.0));
        }

        var cappedVulnerabilityRisk = Math.Min(6.0, vulnerabilityRisk);
        risk += cappedVulnerabilityRisk;

        if (vulnerabilityRisk > cappedVulnerabilityRisk)
        {
            rationale.Add("Vulnerability contribution capped at +6.0");
        }

        if (package.DependencyDepth > 20)
        {
            risk += 3.0;
            rationale.Add(CreateRationale($"Deep dependency chain (depth {package.DependencyDepth})", 3.0));
        }
        else if (package.DependencyDepth > 10)
        {
            risk += 2.0;
            rationale.Add(CreateRationale($"Deep dependency chain (depth {package.DependencyDepth})", 2.0));
        }
        else if (package.DependencyDepth > 0)
        {
            rationale.Add(CreateRationale($"Dependency depth {package.DependencyDepth} stays below the risk threshold", 0.0));
        }

        var transitiveRisk = Math.Min(2.0, package.TransitiveVulnerabilityCount * 0.5);
        risk += transitiveRisk;

        if (package.TransitiveVulnerabilityCount > 0)
        {
            rationale.Add(CreateRationale(
                $"Vulnerable transitive dependencies ({package.TransitiveVulnerabilityCount})",
                transitiveRisk));
        }

        if (package.IsPackageSigned == false)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("Package signature is missing or invalid", 1.0));
        }
        else if (package.IsPackageSigned == true)
        {
            if (package.HasTrustedPackageSignature == false)
            {
                risk += 1.0;
                rationale.Add(CreateRationale("Package is signed but trust verification failed", 1.0));
            }
            else
            {
                rationale.Add(CreateRationale("Package is signed and trust verification succeeded", 0.0));
            }
        }

        if (package.OwnerCreatedAt is DateTimeOffset ownerCreatedAt &&
            ownerCreatedAt > DateTimeOffset.UtcNow.AddYears(-1))
        {
            risk += 1.0;
            rationale.Add(CreateRationale("Repository owner account is less than one year old", 1.0));
        }

        if (!package.OwnerIsOrganization && package.ContributorCount is <= 1)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("Single maintainer on a non-organization account", 1.0));
        }

        if (package.PublishedAt is DateTimeOffset publishedAt &&
            publishedAt < DateTimeOffset.UtcNow.AddMonths(-24))
        {
            risk += 2.0;
            rationale.Add(CreateRationale("Last published release is older than 24 months", 2.0));
        }

        return CreateEvaluation(risk, rationale);
    }

    /// <summary>
    /// Evaluates operational risk based on maintenance status and package activity.
    /// </summary>
    private RiskDimensionEvaluation EvaluateOperationalRisk(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (package.PublishedAt is DateTimeOffset publishedAt)
        {
            if (publishedAt < DateTimeOffset.UtcNow.AddMonths(-24))
            {
                risk += 4.0;
                rationale.Add(CreateRationale("Last release is older than 24 months", 4.0));
            }
            else if (publishedAt < DateTimeOffset.UtcNow.AddMonths(-12))
            {
                risk += 2.0;
                rationale.Add(CreateRationale("Last release is older than 12 months", 2.0));
            }
            else
            {
                rationale.Add(CreateRationale("Release cadence looks current", 0.0));
            }
        }

        if (package.HasReadme != true || package.HasDefaultReadme == true)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("README is missing or appears to be boilerplate", 1.0));
        }
        else
        {
            rationale.Add(CreateRationale("README looks present and non-default", 0.0));
        }

        if (package.HasContributingGuide != true)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("CONTRIBUTING guide is missing", 1.0));
        }
        else
        {
            rationale.Add(CreateRationale("CONTRIBUTING guide is present", 0.0));
        }

        if (package.HasSecurityPolicy != true)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("SECURITY policy is missing", 1.0));
        }
        else
        {
            rationale.Add(CreateRationale("SECURITY policy is present", 0.0));
        }

        if (package.HasChangelog != true || package.HasDefaultChangelog == true)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("CHANGELOG or release notes are missing or low quality", 1.0));
        }
        else
        {
            rationale.Add(CreateRationale("CHANGELOG or release notes are present", 0.0));
        }

        if (package.ContributorCount is < 2)
        {
            risk += 3.0;
            rationale.Add(CreateRationale($"Low contributor count ({package.ContributorCount})", 3.0));
        }
        else if (package.ContributorCount is < 5)
        {
            risk += 2.0;
            rationale.Add(CreateRationale($"Low contributor count ({package.ContributorCount})", 2.0));
        }
        else if (package.ContributorCount is int contributorCount)
        {
            rationale.Add(CreateRationale($"Contributor count is healthy ({contributorCount})", 0.0));
        }

        if (package.TopContributorShare is >= 0.8)
        {
            risk += 2.0;
            rationale.Add(CreateRationale(
                $"Contribution concentration is high (top contributor owns {FormatPercentage(package.TopContributorShare.Value)})",
                2.0));
        }
        else if (package.TopTwoContributorShare is >= 0.95)
        {
            risk += 1.0;
            rationale.Add(CreateRationale(
                $"Contribution concentration is high (top two contributors own {FormatPercentage(package.TopTwoContributorShare.Value)})",
                1.0));
        }

        if (package.OpenBugIssueCount > 25)
        {
            risk += 2.0;
            rationale.Add(CreateRationale($"High number of open bug issues ({package.OpenBugIssueCount})", 2.0));
        }
        else if (package.OpenBugIssueCount > 10)
        {
            risk += 1.0;
            rationale.Add(CreateRationale($"Elevated number of open bug issues ({package.OpenBugIssueCount})", 1.0));
        }

        if (package.StaleCriticalBugIssueCount > 0)
        {
            risk += 2.0;
            rationale.Add(CreateRationale(
                $"Stale critical bug issues remain open ({package.StaleCriticalBugIssueCount})",
                2.0));
        }

        if (package.MedianIssueResponseDays > 30)
        {
            risk += 2.0;
            rationale.Add(CreateRationale(
                $"Median issue response time is slow ({FormatScore(package.MedianIssueResponseDays!.Value)} days)",
                2.0));
        }

        if (package.MedianPullRequestMergeDays > 60)
        {
            risk += 1.0;
            rationale.Add(CreateRationale(
                $"Median pull request merge time is slow ({FormatScore(package.MedianPullRequestMergeDays!.Value)} days)",
                1.0));
        }

        if (package.RecentFailedWorkflowCount is >= 3)
        {
            risk += 1.0;
            rationale.Add(CreateRationale(
                $"Recent CI workflow failures are elevated ({package.RecentFailedWorkflowCount})",
                1.0));
        }

        if (package.HasRecentSuccessfulWorkflowRun == false)
        {
            risk += 2.0;
            rationale.Add(CreateRationale("No recent successful CI workflow run detected", 2.0));
        }
        else if (package.HasRecentSuccessfulWorkflowRun == true)
        {
            rationale.Add(CreateRationale("Recent successful CI workflow run detected", 0.0));
        }

        if (package.DownloadCount is < 1000)
        {
            risk += 3.0;
            rationale.Add(CreateRationale($"Low package popularity ({package.DownloadCount} downloads)", 3.0));
        }
        else if (package.DownloadCount is < 10000)
        {
            risk += 2.0;
            rationale.Add(CreateRationale($"Limited package popularity ({package.DownloadCount} downloads)", 2.0));
        }
        else if (package.DownloadCount is long downloadCount)
        {
            rationale.Add(CreateRationale($"Package popularity is established ({downloadCount} downloads)", 0.0));
        }

        if (package.HasPreOneZeroDependencies)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("Depends on pre-1.0 packages", 1.0));
        }

        if (package.IsMajorVersionBehindLatest)
        {
            risk += 2.0;
            rationale.Add(CreateRationale($"Current package version is behind latest stable ({package.LatestStableVersion})", 2.0));
        }
        else if (package.IsMinorVersionBehindLatest)
        {
            risk += 1.0;
            rationale.Add(CreateRationale($"Current package version is behind latest stable ({package.LatestStableVersion})", 1.0));
        }

        if (package.HasModernTargetFrameworkSupport == false && package.SupportedTargetFrameworks.Length > 0)
        {
            risk += 1.0;
            rationale.Add(CreateRationale(
                $"Target frameworks look dated ({string.Join(", ", package.SupportedTargetFrameworks)})",
                1.0));
        }
        else if (package.HasModernTargetFrameworkSupport == true)
        {
            rationale.Add(CreateRationale("Target frameworks include modern runtimes", 0.0));
        }

        if (package.OpenSsfScore is double openSsfScore)
        {
            if (openSsfScore < 5.0)
            {
                risk += 2.0;
                rationale.Add(CreateRationale($"OpenSSF Scorecard score is low ({FormatScore(openSsfScore)})", 2.0));
            }
            else if (openSsfScore < 7.0)
            {
                risk += 1.0;
                rationale.Add(CreateRationale($"OpenSSF Scorecard score is moderate ({FormatScore(openSsfScore)})", 1.0));
            }
            else
            {
                rationale.Add(CreateRationale($"OpenSSF Scorecard score is strong ({FormatScore(openSsfScore)})", 0.0));
            }
        }

        if (package.HasBranchProtection == false)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("Default branch protection was not detected", 1.0));
        }
        else if (package.HasBranchProtection == true)
        {
            rationale.Add(CreateRationale("Default branch protection was detected", 0.0));
        }

        if (package.HasProvenanceAttestation == false)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("No provenance or attestation workflow signal was detected", 1.0));
        }
        else if (package.HasProvenanceAttestation == true)
        {
            rationale.Add(CreateRationale("Provenance or attestation workflow signal was detected", 0.0));
        }

        if (package.HasRepositoryOwnershipOrRenameChurn == true)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("Repository ownership or rename churn was detected", 1.0));
        }

        return CreateEvaluation(risk, rationale);
    }

    /// <summary>
    /// Determines if a license is considered restrictive (copyleft, commercial restrictions).
    /// </summary>
    private static bool IsRestrictiveLicense(string license)
    {
        var restrictiveLicenses = new[]
        {
            "GPL", "AGPL", "SSPL", "Commons Clause", "BUSL", "BCL"
        };

        return restrictiveLicenses.Any(restrictive => 
            license.Contains(restrictive, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWeakCopyleftLicense(string license)
    {
        var weakCopyleftLicenses = new[]
        {
            "LGPL", "MPL"
        };

        return weakCopyleftLicenses.Any(candidate =>
            license.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines if a license is considered permissive (MIT, Apache, BSD).
    /// </summary>
    private static bool IsPermissiveLicense(string license)
    {
        var permissiveLicenses = new[]
        {
            "MIT", "Apache", "BSD", "ISC", "Unlicense", "WTFPL", "CC0"
        };

        return permissiveLicenses.Any(permissive => 
            license.Contains(permissive, StringComparison.OrdinalIgnoreCase));
    }

    private static RiskDimensionEvaluation CreateEvaluation(double risk, List<string> rationale)
    {
        var cappedRisk = Math.Min(risk, 10.0);
        if (rationale.Count == 0)
        {
            rationale.Add(CreateRationale("No elevated signals detected", 0.0));
        }

        if (risk > cappedRisk)
        {
            rationale.Add($"Dimension score capped at {FormatScore(cappedRisk)}/10");
        }

        return new RiskDimensionEvaluation(cappedRisk, rationale.ToArray());
    }

    private static string CreateRationale(string description, double contribution)
    {
        return $"{description} (+{FormatScore(contribution)})";
    }

    private static string FormatScore(double value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string FormatPercentage(double value)
    {
        return value.ToString("P0", CultureInfo.InvariantCulture);
    }
}
