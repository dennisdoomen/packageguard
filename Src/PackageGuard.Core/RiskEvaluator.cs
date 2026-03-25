using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

/// <summary>
/// Evaluates and calculates risk metrics for software packages.
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

        if (string.IsNullOrEmpty(package.License) || package.License.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
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

        if (package.HasValidLicenseUrl is false || string.IsNullOrEmpty(package.LicenseUrl))
        {
            risk += 1.0;
            rationale.Add(CreateRationale("Missing or invalid license URL", 1.0));
        }
        else if (package.HasValidLicenseUrl is true)
        {
            rationale.Add(CreateRationale("Valid license URL", 0.0));
        }

        if (package.IsLicensePolicyCompatible is false)
        {
            risk += 3.0;
            rationale.Add(CreateRationale("License is incompatible with configured policy", 3.0));
        }
        else if (package.IsLicensePolicyCompatible is true)
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
            risk += 3.0;
            rationale.Add(CreateRationale("No public repository available", 3.0));
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
            vulnerabilityRisk += 0.5;
            rationale.Add(CreateRationale("Package has a recent vulnerability fix window (<90 days)", 0.5));
        }

        if (package is { VulnerabilityCount: > 0, HasAvailableSecurityFix: true })
        {
            vulnerabilityRisk += 0.5;
            rationale.Add(CreateRationale("A security fix is available for a known vulnerability", 0.5));
        }

        if (package.MedianVulnerabilityFixDays is > 180)
        {
            vulnerabilityRisk += 1.0;
            rationale.Add(CreateRationale(
                $"Median vulnerability fix time is slow ({FormatScore(package.MedianVulnerabilityFixDays.Value)} days)",
                1.0));
        }
        else if (package.MedianVulnerabilityFixDays is > 60)
        {
            vulnerabilityRisk += 0.5;
            rationale.Add(CreateRationale(
                $"Median vulnerability fix time is elevated ({FormatScore(package.MedianVulnerabilityFixDays.Value)} days)",
                0.5));
        }
        else if (package.MedianVulnerabilityFixDays != null)
        {
            double fixDays = package.MedianVulnerabilityFixDays.Value;
            rationale.Add(CreateRationale($"Median vulnerability fix time looks reasonable ({FormatScore(fixDays)} days)", 0.0));
        }

        var cappedVulnerabilityRisk = Math.Min(6.0, vulnerabilityRisk);
        risk += cappedVulnerabilityRisk;

        if (vulnerabilityRisk > cappedVulnerabilityRisk)
        {
            rationale.Add("Vulnerability contribution capped at +6.0");
        }

        if (package.DependencyDepth > 20)
        {
            risk += 2.5;
            rationale.Add(CreateRationale($"Deep dependency chain (depth {package.DependencyDepth})", 2.5));
        }
        else if (package.DependencyDepth > 10)
        {
            risk += 1.5;
            rationale.Add(CreateRationale($"Deep dependency chain (depth {package.DependencyDepth})", 1.5));
        }
        else if (package.DependencyDepth > 0)
        {
            rationale.Add(CreateRationale($"Dependency depth {package.DependencyDepth} stays below the risk threshold", 0.0));
        }

        var transitiveRisk = Math.Min(1.5, package.TransitiveVulnerabilityCount * 0.5);
        risk += transitiveRisk;

        if (package.TransitiveVulnerabilityCount > 0)
        {
            rationale.Add(CreateRationale(
                $"Vulnerable transitive dependencies ({package.TransitiveVulnerabilityCount})",
                transitiveRisk));
        }

        if (package.StaleTransitiveDependencyCount is > 5)
        {
            risk += 0.75;
            rationale.Add(CreateRationale(
                $"Multiple stale transitive dependencies were detected ({package.StaleTransitiveDependencyCount})",
                0.75));
        }
        else if (package.StaleTransitiveDependencyCount is > 0)
        {
            risk += 0.25;
            rationale.Add(CreateRationale(
                $"Some stale transitive dependencies were detected ({package.StaleTransitiveDependencyCount})",
                0.25));
        }

        if (package.AbandonedTransitiveDependencyCount is > 0)
        {
            var abandonedRisk = Math.Min(1.0, package.AbandonedTransitiveDependencyCount.Value * 0.5);
            risk += abandonedRisk;
            rationale.Add(CreateRationale(
                $"Potentially abandoned risky transitive dependencies were detected ({package.AbandonedTransitiveDependencyCount})",
                abandonedRisk));
        }

        if (package.UnmaintainedCriticalTransitiveDependencyCount is > 0)
        {
            int unmaintainedCriticalTransitiveDependencyCount = package.UnmaintainedCriticalTransitiveDependencyCount.Value;
            double criticalTransitiveRisk = Math.Min(1.0, unmaintainedCriticalTransitiveDependencyCount * 0.5);
            risk += criticalTransitiveRisk;
            rationale.Add(CreateRationale(
                $"Unmaintained critical transitive dependencies were detected ({unmaintainedCriticalTransitiveDependencyCount})",
                criticalTransitiveRisk));
        }

        if (package.IsPackageSigned is false)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("Package signature is missing or invalid", 0.5));
        }
        else if (package.IsPackageSigned is true)
        {
            if (package.HasTrustedPackageSignature is false)
            {
                risk += 1.0;
                rationale.Add(CreateRationale("Package is signed but trust verification failed", 1.0));
            }
            else
            {
                rationale.Add(CreateRationale("Package is signed and trust verification succeeded", 0.0));
            }
        }

        if (package.HasVerifiedPublisher is false)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("Verified publisher signal was not detected", 0.5));
        }
        else if (package.HasVerifiedPublisher is true)
        {
            rationale.Add(CreateRationale("Verified publisher signal was detected", 0.0));
        }

        if (package.HasNativeBinaryAssets is true)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("Package contains native or binary assets that may increase supply-chain exposure", 0.5));
        }

        if (package.VerifiedCommitRatio is < 0.5)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Verified commit coverage is limited ({FormatPercentage(package.VerifiedCommitRatio.Value)})",
                0.5));
        }
        else if (package.VerifiedCommitRatio != null)
        {
            double verifiedCommitRatio = package.VerifiedCommitRatio.Value;
            rationale.Add(CreateRationale($"Verified commit coverage looks healthy ({FormatPercentage(verifiedCommitRatio)})", 0.0));
        }

        if (package.IsDeprecated is true)
        {
            risk += 0.75;
            rationale.Add(CreateRationale("The package version is marked as deprecated", 0.75));
        }

        if (package.OwnerCreatedAt != null &&
            package.OwnerCreatedAt.Value > DateTimeOffset.UtcNow.AddYears(-1))
        {
            risk += 0.5;
            rationale.Add(CreateRationale("Repository owner account is less than one year old", 0.5));
        }

        if (!package.OwnerIsOrganization && (package.RecentMaintainerCount ?? package.ContributorCount) is <= 1)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("Single maintainer on a non-organization account", 0.5));
        }

        if (package.PublishedAt != null &&
            package.PublishedAt.Value < DateTimeOffset.UtcNow.AddMonths(-24))
        {
            risk += 1.0;
            rationale.Add(CreateRationale("Last published release is older than 24 months", 1.0));
        }

        if (package.HasDetailedSecurityPolicy is false)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("SECURITY policy lacks detailed reporting guidance", 0.25));
        }

        if (package.HasCoordinatedDisclosure is false)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("No coordinated disclosure signal was detected", 0.25));
        }

        if (package.HasProvenanceAttestation is false)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("No provenance or attestation workflow signal was detected", 0.5));
        }
        else if (package.HasProvenanceAttestation is true)
        {
            rationale.Add(CreateRationale("Provenance or attestation workflow signal was detected", 0.0));
        }

        if (package.HasReproducibleBuildSignal is false)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("No reproducible-build or deterministic-build signal was detected", 0.25));
        }

        if (package.HasVerifiedReleaseSignature is false)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("Verified release signature signal was not detected", 0.25));
        }
        else if (package.HasVerifiedReleaseSignature is true)
        {
            rationale.Add(CreateRationale("Verified release signature signal was detected", 0.0));
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

        if (package.PublishedAt != null)
        {
            DateTimeOffset publishedAt = package.PublishedAt.Value;
            if (publishedAt < DateTimeOffset.UtcNow.AddMonths(-24))
            {
                risk += 2.0;
                rationale.Add(CreateRationale("Last release is older than 24 months", 2.0));
            }
            else if (publishedAt < DateTimeOffset.UtcNow.AddMonths(-12))
            {
                risk += 1.0;
                rationale.Add(CreateRationale("Last release is older than 12 months", 1.0));
            }
            else
            {
                rationale.Add(CreateRationale("Release cadence looks current", 0.0));
            }
        }

        if (package.PrereleaseRatio is > 0.5)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"High prerelease ratio detected ({FormatPercentage(package.PrereleaseRatio.Value)})",
                0.5));
        }

        if (package.RapidReleaseCorrectionCount is > 1)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Rapid release corrections were detected ({package.RapidReleaseCorrectionCount})",
                0.5));
        }

        if (package.MeanReleaseIntervalDays is > 365)
        {
            risk += 1.0;
            rationale.Add(CreateRationale(
                $"Mean release interval is long ({FormatScore(package.MeanReleaseIntervalDays.Value)} days)",
                1.0));
        }
        else if (package.MeanReleaseIntervalDays is > 180)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Mean release interval is elevated ({FormatScore(package.MeanReleaseIntervalDays.Value)} days)",
                0.5));
        }

        if (package.HasSemVerReleaseTags is false)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("Recent release tags do not consistently follow semantic versioning", 0.5));
        }
        else if (package.HasSemVerReleaseTags is true)
        {
            rationale.Add(CreateRationale("Recent release tags follow semantic versioning", 0.0));
        }

        if (package.MajorReleaseRatio is > 0.40)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"A high share of semver release transitions were major-version jumps ({FormatPercentage(package.MajorReleaseRatio.Value)})",
                0.5));
        }

        if (package.HasReadme is not true || package.HasDefaultReadme is true)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("README is missing or appears to be boilerplate", 0.5));
        }
        else
        {
            rationale.Add(CreateRationale("README looks present and non-default", 0.0));
        }

        if (package.ReadmeUpdatedAt != null &&
            package.ReadmeUpdatedAt.Value < DateTimeOffset.UtcNow.AddMonths(-18))
        {
            risk += 0.25;
            rationale.Add(CreateRationale("README has not been refreshed recently", 0.25));
        }

        if (package.HasContributingGuide != true)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("CONTRIBUTING guide is missing", 0.5));
        }
        else
        {
            rationale.Add(CreateRationale("CONTRIBUTING guide is present", 0.0));
        }

        if (package.HasSecurityPolicy != true)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("SECURITY policy is missing", 0.5));
        }
        else
        {
            rationale.Add(CreateRationale("SECURITY policy is present", 0.0));
        }

        if (package.HasDetailedSecurityPolicy is false)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("SECURITY policy lacks concrete response instructions", 0.25));
        }

        bool hasAcceptableReleaseHistory = package.HasReleaseNotes is true ||
                                           (package.HasChangelog is true && package.HasDefaultChangelog is not true);
        if (!hasAcceptableReleaseHistory)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("CHANGELOG or release notes are missing or low quality", 0.5));
        }
        else
        {
            rationale.Add(CreateRationale("CHANGELOG or release notes are present", 0.0));
        }

        if (package.ChangelogUpdatedAt != null &&
            package.ChangelogUpdatedAt.Value < DateTimeOffset.UtcNow.AddMonths(-18))
        {
            risk += 0.25;
            rationale.Add(CreateRationale("CHANGELOG has not been refreshed recently", 0.25));
        }

        if (package.ContributorCount is < 2)
        {
            risk += 2.5;
            rationale.Add(CreateRationale($"Low contributor count ({package.ContributorCount})", 2.5));
        }
        else if (package.ContributorCount is < 5)
        {
            risk += 1.5;
            rationale.Add(CreateRationale($"Low contributor count ({package.ContributorCount})", 1.5));
        }
        else if (package.ContributorCount != null)
        {
            int contributorCount = package.ContributorCount.Value;
            rationale.Add(CreateRationale($"Contributor count is healthy ({contributorCount})", 0.0));
        }

        if (package.RecentMaintainerCount is < 2)
        {
            risk += 1.0;
            rationale.Add(CreateRationale($"Very few active maintainers in the last 6 months ({package.RecentMaintainerCount})", 1.0));
        }
        else if (package.RecentMaintainerCount is < 4)
        {
            risk += 0.5;
            rationale.Add(CreateRationale($"Limited active maintainer pool in the last 6 months ({package.RecentMaintainerCount})", 0.5));
        }

        if (package.MedianMaintainerActivityDays is > 180)
        {
            risk += 1.0;
            rationale.Add(CreateRationale(
                $"Median maintainer inactivity is high ({FormatScore(package.MedianMaintainerActivityDays.Value)} days since last activity)",
                1.0));
        }
        else if (package.MedianMaintainerActivityDays is > 90)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Median maintainer inactivity is elevated ({FormatScore(package.MedianMaintainerActivityDays.Value)} days since last activity)",
                0.5));
        }

        if (package.TopContributorShare is >= 0.8)
        {
            risk += 1.5;
            rationale.Add(CreateRationale(
                $"Contribution concentration is high (top contributor owns {FormatPercentage(package.TopContributorShare.Value)})",
                1.5));
        }
        else if (package.TopTwoContributorShare is >= 0.95)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Contribution concentration is high (top two contributors own {FormatPercentage(package.TopTwoContributorShare.Value)})",
                0.5));
        }

        if (package.OpenBugIssueCount > 25)
        {
            risk += 1.5;
            rationale.Add(CreateRationale($"High number of open bug issues ({package.OpenBugIssueCount})", 1.5));
        }
        else if (package.OpenBugIssueCount > 10)
        {
            risk += 0.5;
            rationale.Add(CreateRationale($"Elevated number of open bug issues ({package.OpenBugIssueCount})", 0.5));
        }

        if (package.StaleCriticalBugIssueCount > 0)
        {
            risk += 1.5;
            rationale.Add(CreateRationale(
                $"Stale critical bug issues remain open ({package.StaleCriticalBugIssueCount})",
                1.5));
        }

        double? closureRate = GetBugClosureRate(package);
        if (closureRate != null)
        {
            double bugClosureRate = closureRate.Value;
            if (bugClosureRate < 0.35)
            {
                risk += 1.0;
                rationale.Add(CreateRationale($"Bug closure rate is low ({FormatPercentage(bugClosureRate)})", 1.0));
            }
            else if (bugClosureRate < 0.60)
            {
                risk += 0.5;
                rationale.Add(CreateRationale($"Bug closure rate is moderate ({FormatPercentage(bugClosureRate)})", 0.5));
            }
            else
            {
                rationale.Add(CreateRationale($"Bug closure rate looks healthy ({FormatPercentage(bugClosureRate)})", 0.0));
            }
        }

        double? reopenRate = GetBugReopenRate(package);
        if (reopenRate > 0.20)
        {
            risk += 0.75;
            rationale.Add(CreateRationale($"Bug reopen rate is elevated ({FormatPercentage(reopenRate.Value)})", 0.75));
        }

        if (package.MedianIssueResponseDays > 30)
        {
            risk += 1.0;
            rationale.Add(CreateRationale(
                $"Median issue response time is slow ({FormatScore(package.MedianIssueResponseDays!.Value)} days)",
                1.0));
        }
        else if (package.MedianIssueResponseDays != null)
        {
            double responseDays = package.MedianIssueResponseDays.Value;
            rationale.Add(CreateRationale($"Median issue response time looks healthy ({FormatScore(responseDays)} days)", 0.0));
        }

        if (package.MedianCriticalIssueResponseDays > 7)
        {
            risk += 1.0;
            rationale.Add(CreateRationale(
                $"Critical issue response time is slow ({FormatScore(package.MedianCriticalIssueResponseDays!.Value)} days)",
                1.0));
        }

        if (package.IssueResponseCoverage is < 0.5)
        {
            risk += 0.75;
            rationale.Add(CreateRationale(
                $"Maintainer response coverage is low ({FormatPercentage(package.IssueResponseCoverage.Value)})",
                0.75));
        }

        if (package.IssueTriageWithinSevenDaysRate is < 0.5)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Issue triage within 7 days is low ({FormatPercentage(package.IssueTriageWithinSevenDaysRate.Value)})",
                0.5));
        }

        if (package.MedianOpenBugAgeDays > 180)
        {
            risk += 0.75;
            rationale.Add(CreateRationale(
                $"Median age of open bugs is high ({FormatScore(package.MedianOpenBugAgeDays!.Value)} days)",
                0.75));
        }

        if (package.MedianPullRequestMergeDays > 60)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Median pull request merge time is slow ({FormatScore(package.MedianPullRequestMergeDays!.Value)} days)",
                0.5));
        }

        if (package.RecentFailedWorkflowCount is >= 3)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Recent CI workflow failures are elevated ({package.RecentFailedWorkflowCount})",
                0.5));
        }

        if (package.WorkflowFailureRate is > 0.5)
        {
            risk += 0.75;
            rationale.Add(CreateRationale(
                $"CI workflow failure rate is elevated ({FormatPercentage(package.WorkflowFailureRate.Value)})",
                0.75));
        }

        if (package.HasFlakyWorkflowPattern is true)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("CI workflow history shows a potentially flaky failure pattern", 0.5));
        }

        if (package.HasRecentSuccessfulWorkflowRun is false)
        {
            risk += 1.5;
            rationale.Add(CreateRationale("No recent successful CI workflow run detected", 1.5));
        }
        else if (package.HasRecentSuccessfulWorkflowRun is true)
        {
            rationale.Add(CreateRationale("Recent successful CI workflow run detected", 0.0));
        }

        if (package.RequiredStatusCheckCount is 0)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("No required status checks were detected on the default branch", 0.5));
        }
        else if (package.RequiredStatusCheckCount is > 0)
        {
            rationale.Add(CreateRationale($"Required status checks are configured ({package.RequiredStatusCheckCount})", 0.0));
        }

        if (package.WorkflowPlatformCount is < 2)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("CI workflow matrix breadth looks limited", 0.25));
        }

        if (package.HasCoverageWorkflowSignal is false)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("No coverage workflow signal was detected", 0.25));
        }

        if (package.HasTestSignal is false)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("No explicit test execution signal was detected", 0.5));
        }

        if (package.HasDependencyUpdateAutomation is false)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("No dependency update automation signal was detected", 0.25));
        }

        if (package.DownloadCount is < 1000)
        {
            risk += 2.0;
            rationale.Add(CreateRationale($"Low package popularity ({package.DownloadCount} downloads)", 2.0));
        }
        else if (package.DownloadCount is < 10000)
        {
            risk += 1.0;
            rationale.Add(CreateRationale($"Limited package popularity ({package.DownloadCount} downloads)", 1.0));
        }
        else if (package.DownloadCount != null)
        {
            long downloadCount = package.DownloadCount.Value;
            rationale.Add(CreateRationale($"Package popularity is established ({downloadCount} downloads)", 0.0));
        }

        if (package.HasPreOneZeroDependencies)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("Depends on pre-1.0 packages", 0.5));
        }

        if (package.StaleTransitiveDependencyCount is > 0)
        {
            risk += 0.25;
            rationale.Add(CreateRationale($"Stale transitive dependencies were detected ({package.StaleTransitiveDependencyCount})", 0.25));
        }

        if (package.AbandonedTransitiveDependencyCount is > 0)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Potentially abandoned transitive dependencies were detected ({package.AbandonedTransitiveDependencyCount})",
                0.5));
        }

        if (package.DeprecatedTransitiveDependencyCount is > 0)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Deprecated transitive dependencies were detected ({package.DeprecatedTransitiveDependencyCount})",
                0.5));
        }

        if (package.UnmaintainedCriticalTransitiveDependencyCount is > 0)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Unmaintained critical transitive dependencies were detected ({package.UnmaintainedCriticalTransitiveDependencyCount})",
                0.5));
        }

        if (package.IsDeprecated is true)
        {
            risk += 1.0;
            rationale.Add(CreateRationale("The package version is marked as deprecated", 1.0));
        }

        if (package.IsMajorVersionBehindLatest)
        {
            risk += 1.5;
            rationale.Add(CreateRationale($"Current package version is behind latest stable ({package.LatestStableVersion})", 1.5));
        }
        else if (package.IsMinorVersionBehindLatest)
        {
            risk += 0.5;
            rationale.Add(CreateRationale($"Current package version is behind latest stable ({package.LatestStableVersion})", 0.5));
        }

        if (package is { HasModernTargetFrameworkSupport: false, SupportedTargetFrameworks: [_, ..] })
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"Target frameworks look dated ({string.Join(", ", package.SupportedTargetFrameworks)})",
                0.5));
        }
        else if (package.HasModernTargetFrameworkSupport is true)
        {
            rationale.Add(CreateRationale("Target frameworks include modern runtimes", 0.0));
        }

        if (package.OpenSsfScore != null)
        {
            double openSsfScore = package.OpenSsfScore.Value;
            if (openSsfScore < 5.0)
            {
                risk += 1.5;
                rationale.Add(CreateRationale($"OpenSSF Scorecard score is low ({FormatScore(openSsfScore)})", 1.5));
            }
            else if (openSsfScore < 7.0)
            {
                risk += 0.5;
                rationale.Add(CreateRationale($"OpenSSF Scorecard score is moderate ({FormatScore(openSsfScore)})", 0.5));
            }
            else
            {
                rationale.Add(CreateRationale($"OpenSSF Scorecard score is strong ({FormatScore(openSsfScore)})", 0.0));
            }
        }

        if (package.HasBranchProtection is false)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("Default branch protection was not detected", 0.5));
        }
        else if (package.HasBranchProtection is true)
        {
            rationale.Add(CreateRationale("Default branch protection was detected", 0.0));
        }

        if (package.HasRepositoryOwnershipOrRenameChurn is true)
        {
            risk += 0.5;
            rationale.Add(CreateRationale("Repository ownership or rename churn was detected", 0.5));
        }

        if (package.HasVerifiedReleaseSignature is false)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("Verified release signature signal was not detected", 0.25));
        }

        if (package.HasVerifiedPublisher is false)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("Verified publisher signal was not detected", 0.25));
        }

        if (package.HasReproducibleBuildSignal is false)
        {
            risk += 0.25;
            rationale.Add(CreateRationale("No reproducible-build signal was detected", 0.25));
        }

        if (package.VersionUpdateLagDays is > 365)
        {
            risk += 1.0;
            rationale.Add(CreateRationale(
                $"The current version trails the latest stable release by a long time ({FormatScore(package.VersionUpdateLagDays.Value)} days)",
                1.0));
        }
        else if (package.VersionUpdateLagDays is > 90)
        {
            risk += 0.5;
            rationale.Add(CreateRationale(
                $"The current version trails the latest stable release ({FormatScore(package.VersionUpdateLagDays.Value)} days)",
                0.5));
        }

        if (package is { ExternalContributionRate: <= 0.05, ContributorCount: > 5 })
        {
            risk += 0.25;
            rationale.Add(CreateRationale(
                $"External contribution rate is low ({FormatPercentage(package.ExternalContributionRate!.Value)})",
                0.25));
        }
        else if (package.ExternalContributionRate is > 0.15)
        {
            rationale.Add(CreateRationale(
                $"External contribution rate looks healthy ({FormatPercentage(package.ExternalContributionRate.Value)})",
                0.0));
        }

        if (package is { UniqueReviewerCount: < 2, ContributorCount: > 3 })
        {
            risk += 0.25;
            rationale.Add(CreateRationale(
                $"Reviewer diversity looks limited ({package.UniqueReviewerCount!.Value} unique reviewers)",
                0.25));
        }
        else if (package.UniqueReviewerCount is > 1)
        {
            rationale.Add(CreateRationale(
                $"Reviewer diversity looks healthy ({package.UniqueReviewerCount} unique reviewers)",
                0.0));
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

    private static double? GetBugClosureRate(PackageInfo package)
    {
        if (package.ClosedBugIssueCountLast90Days is null || package.OpenBugIssueCount is null)
        {
            return null;
        }

        int closed = package.ClosedBugIssueCountLast90Days.Value;
        int open = package.OpenBugIssueCount.Value;
        int total = closed + open;
        return total > 0 ? closed / (double)total : null;
    }

    private static double? GetBugReopenRate(PackageInfo package)
    {
        if (package.ClosedBugIssueCountLast90Days is not > 0 || package.ReopenedBugIssueCountLast90Days is null)
        {
            return null;
        }

        return package.ReopenedBugIssueCountLast90Days.Value / (double)package.ClosedBugIssueCountLast90Days.Value;
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
