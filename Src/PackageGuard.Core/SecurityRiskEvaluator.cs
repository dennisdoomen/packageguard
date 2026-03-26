namespace PackageGuard.Core;

/// <summary>
/// Evaluates security risk based on known vulnerabilities, source transparency, and supply-chain signals.
/// </summary>
internal sealed class SecurityRiskEvaluator : IEvaluateRiskDimension
{
    /// <inheritdoc/>
    public RiskDimensionEvaluation Evaluate(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (string.IsNullOrEmpty(package.RepositoryUrl))
        {
            risk += 3.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No public repository available", 3.0));
        }
        else
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Public repository available", 0.0));
        }

        double vulnerabilityRisk = 0;
        if (package.VulnerabilityCount > 0)
        {
            double severityContribution = Math.Min(4.0, package.MaxVulnerabilitySeverity / 2.0);
            vulnerabilityRisk += severityContribution;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Known vulnerabilities found ({package.VulnerabilityCount}, max severity {RiskEvaluationHelpers.FormatScore(package.MaxVulnerabilitySeverity)})",
                severityContribution));
        }
        else
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No direct vulnerabilities reported", 0.0));
        }

        if (package.HasPatchedVulnerabilityInLast90Days)
        {
            vulnerabilityRisk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Package has a recent vulnerability fix window (<90 days)", 0.5));
        }

        if (package is { VulnerabilityCount: > 0, HasAvailableSecurityFix: true })
        {
            vulnerabilityRisk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("A security fix is available for a known vulnerability", 0.5));
        }

        if (package.MedianVulnerabilityFixDays is > 180)
        {
            vulnerabilityRisk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Median vulnerability fix time is slow ({RiskEvaluationHelpers.FormatScore(package.MedianVulnerabilityFixDays.Value)} days)",
                1.0));
        }
        else if (package.MedianVulnerabilityFixDays is > 60)
        {
            vulnerabilityRisk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Median vulnerability fix time is elevated ({RiskEvaluationHelpers.FormatScore(package.MedianVulnerabilityFixDays.Value)} days)",
                0.5));
        }
        else if (package.MedianVulnerabilityFixDays != null)
        {
            double fixDays = package.MedianVulnerabilityFixDays.Value;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Median vulnerability fix time looks reasonable ({RiskEvaluationHelpers.FormatScore(fixDays)} days)", 0.0));
        }

        double cappedVulnerabilityRisk = Math.Min(6.0, vulnerabilityRisk);
        risk += cappedVulnerabilityRisk;

        if (vulnerabilityRisk > cappedVulnerabilityRisk)
        {
            rationale.Add("Vulnerability contribution capped at +6.0");
        }

        if (package.DependencyDepth > 20)
        {
            risk += 2.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Deep dependency chain (depth {package.DependencyDepth})", 2.5));
        }
        else if (package.DependencyDepth > 10)
        {
            risk += 1.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Deep dependency chain (depth {package.DependencyDepth})", 1.5));
        }
        else if (package.DependencyDepth > 0)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Dependency depth {package.DependencyDepth} stays below the risk threshold", 0.0));
        }

        double transitiveRisk = Math.Min(1.5, package.TransitiveVulnerabilityCount * 0.5);
        risk += transitiveRisk;

        if (package.TransitiveVulnerabilityCount > 0)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Vulnerable transitive dependencies ({package.TransitiveVulnerabilityCount})",
                transitiveRisk));
        }

        if (package.StaleTransitiveDependencyCount is > 5)
        {
            risk += 0.75;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Multiple stale transitive dependencies were detected ({package.StaleTransitiveDependencyCount})",
                0.75));
        }
        else if (package.StaleTransitiveDependencyCount is > 0)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Some stale transitive dependencies were detected ({package.StaleTransitiveDependencyCount})",
                0.25));
        }

        if (package.AbandonedTransitiveDependencyCount is > 0)
        {
            double abandonedRisk = Math.Min(1.0, package.AbandonedTransitiveDependencyCount.Value * 0.5);
            risk += abandonedRisk;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Potentially abandoned risky transitive dependencies were detected ({package.AbandonedTransitiveDependencyCount})",
                abandonedRisk));
        }

        if (package.UnmaintainedCriticalTransitiveDependencyCount is > 0)
        {
            int count = package.UnmaintainedCriticalTransitiveDependencyCount.Value;
            double criticalTransitiveRisk = Math.Min(1.0, count * 0.5);
            risk += criticalTransitiveRisk;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Unmaintained critical transitive dependencies were detected ({count})",
                criticalTransitiveRisk));
        }

        if (package.IsPackageSigned is false)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Package signature is missing or invalid", 0.5));
        }
        else if (package.IsPackageSigned is true)
        {
            if (package.HasTrustedPackageSignature is false)
            {
                risk += 1.0;
                rationale.Add(RiskEvaluationHelpers.CreateRationale("Package is signed but trust verification failed", 1.0));
            }
            else
            {
                rationale.Add(RiskEvaluationHelpers.CreateRationale("Package is signed and trust verification succeeded", 0.0));
            }
        }

        if (package.HasVerifiedPublisher is false)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Verified publisher signal was not detected", 0.5));
        }
        else if (package.HasVerifiedPublisher is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Verified publisher signal was detected", 0.0));
        }

        if (package.HasNativeBinaryAssets is true)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Package contains native or binary assets that may increase supply-chain exposure", 0.5));
        }

        if (package.VerifiedCommitRatio is < 0.5)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Verified commit coverage is limited ({RiskEvaluationHelpers.FormatPercentage(package.VerifiedCommitRatio.Value)})",
                0.5));
        }
        else if (package.VerifiedCommitRatio != null)
        {
            double verifiedCommitRatio = package.VerifiedCommitRatio.Value;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Verified commit coverage looks healthy ({RiskEvaluationHelpers.FormatPercentage(verifiedCommitRatio)})", 0.0));
        }

        if (package.IsDeprecated is true)
        {
            risk += 0.75;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("The package version is marked as deprecated", 0.75));
        }

        if (package.OwnerCreatedAt != null &&
            package.OwnerCreatedAt.Value > DateTimeOffset.UtcNow.AddYears(-1))
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Repository owner account is less than one year old", 0.5));
        }

        if (!package.OwnerIsOrganization && (package.RecentMaintainerCount ?? package.ContributorCount) is <= 1)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Single maintainer on a non-organization account", 0.5));
        }

        if (package.PublishedAt != null &&
            package.PublishedAt.Value < DateTimeOffset.UtcNow.AddMonths(-24))
        {
            risk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Last published release is older than 24 months", 1.0));
        }

        if (package.HasDetailedSecurityPolicy is false)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("SECURITY policy lacks detailed reporting guidance", 0.25));
        }

        if (package.HasCoordinatedDisclosure is false)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No coordinated disclosure signal was detected", 0.25));
        }

        if (package.HasProvenanceAttestation is false)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No provenance or attestation workflow signal was detected", 0.5));
        }
        else if (package.HasProvenanceAttestation is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Provenance or attestation workflow signal was detected", 0.0));
        }

        if (package.HasReproducibleBuildSignal is false)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No reproducible-build or deterministic-build signal was detected", 0.25));
        }

        if (package.HasVerifiedReleaseSignature is false)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Verified release signature signal was not detected", 0.25));
        }
        else if (package.HasVerifiedReleaseSignature is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Verified release signature signal was detected", 0.0));
        }

        return RiskEvaluationHelpers.CreateEvaluation(risk, rationale);
    }
}
