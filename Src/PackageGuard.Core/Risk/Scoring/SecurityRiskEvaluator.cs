using PackageGuard.Core.Package;

namespace PackageGuard.Core.Risk.Scoring;

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
            risk += RiskEvaluationHelpers.AddContribution(rationale, "No public repository available", 3.0);
        }
        else
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Public repository available", 0.0));
        }

        risk += EvaluateVulnerabilityRisk(package, rationale);
        risk += EvaluateExploitRisk(package, rationale);
        risk += EvaluateTransitiveDependencyRisk(package, rationale);
        risk += EvaluateSupplyChainRisk(package, rationale);
        risk += EvaluateOwnershipRisk(package, rationale);

        return RiskEvaluationHelpers.CreateEvaluation(risk, rationale);
    }

    private static double EvaluateVulnerabilityRisk(PackageInfo package, List<string> rationale)
    {
        double vulnerabilityRisk = 0;
        if (package.VulnerabilityCount > 0)
        {
            double severityContribution = Math.Min(4.0, package.MaxVulnerabilitySeverity / 2.0);
            vulnerabilityRisk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Known vulnerabilities found ({package.VulnerabilityCount}, max severity {RiskEvaluationHelpers.FormatScore(package.MaxVulnerabilitySeverity)})",
                severityContribution);
        }
        else
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No direct vulnerabilities reported", 0.0));
        }

        if (package.HasPatchedVulnerabilityInLast90Days)
        {
            vulnerabilityRisk += RiskEvaluationHelpers.AddContribution(rationale,
                "Package has a recent vulnerability fix window (<90 days)", 0.5);
        }

        if (package is { VulnerabilityCount: > 0, HasAvailableSecurityFix: true })
        {
            vulnerabilityRisk += RiskEvaluationHelpers.AddContribution(rationale,
                "A security fix is available for a known vulnerability", 0.5);
        }

        if (package.MedianVulnerabilityFixDays is > 180)
        {
            vulnerabilityRisk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Median time between vulnerability advisory publication and its last update is slow ({RiskEvaluationHelpers.FormatScore(package.MedianVulnerabilityFixDays.Value)} days)",
                1.0);
        }
        else if (package.MedianVulnerabilityFixDays is > 60)
        {
            vulnerabilityRisk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Median time between vulnerability advisory publication and its last update is elevated ({RiskEvaluationHelpers.FormatScore(package.MedianVulnerabilityFixDays.Value)} days)",
                0.5);
        }
        else if (package.MedianVulnerabilityFixDays != null)
        {
            double fixDays = package.MedianVulnerabilityFixDays.Value;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Median time between vulnerability advisory publication and its last update looks reasonable ({RiskEvaluationHelpers.FormatScore(fixDays)} days)", 0.0));
        }

        double cappedVulnerabilityRisk = Math.Min(6.0, vulnerabilityRisk);

        if (vulnerabilityRisk > cappedVulnerabilityRisk)
        {
            rationale.Add("Vulnerability contribution capped at +6.0");
        }

        return cappedVulnerabilityRisk;
    }

    /// <summary>
    /// Scores how likely the known vulnerabilities are to be exploited, on top of how severe they are. Kept outside
    /// the vulnerability cap so that a known-exploited vulnerability always weighs in.
    /// </summary>
    private static double EvaluateExploitRisk(PackageInfo package, List<string> rationale)
    {
        double risk = 0;

        if (package.KnownExploitedCveIds is { Length: > 0 } exploitedCveIds)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Vulnerabilities are on the CISA Known Exploited Vulnerabilities list ({string.Join(", ", exploitedCveIds)})",
                3.0);
        }

        if (package.MaxEpssScore is > 0 and var epss)
        {
            double epssRisk = epss * 1.5;
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Highest EPSS exploitation probability within 30 days is {RiskEvaluationHelpers.FormatScore(epss * 100)}%",
                epssRisk);
        }

        return risk;
    }

    private static double EvaluateTransitiveDependencyRisk(PackageInfo package, List<string> rationale)
    {
        double risk = 0;

        if (package.DependencyDepth > 20)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Deep dependency chain (depth {package.DependencyDepth})", 2.5);
        }
        else if (package.DependencyDepth > 10)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Deep dependency chain (depth {package.DependencyDepth})", 1.5);
        }
        else if (package.DependencyDepth > 0)
        {
            rationale.Add(
                RiskEvaluationHelpers.CreateRationale(
                    $"Dependency depth {package.DependencyDepth} stays below the risk threshold", 0.0));
        }

        double transitiveRisk = Math.Min(1.5, package.TransitiveVulnerabilityCount * 0.5);
        risk += transitiveRisk;

        if (package.TransitiveVulnerabilityCount > 0)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Vulnerable transitive dependencies ({package.TransitiveVulnerabilityCount})",
                transitiveRisk));
        }

        if (package.AbandonedTransitiveDependencyCount is > 0)
        {
            double abandonedRisk = Math.Min(1.0, package.AbandonedTransitiveDependencyCount.Value * 0.5);
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Potentially abandoned risky transitive dependencies were detected ({package.AbandonedTransitiveDependencyCount})",
                abandonedRisk);
        }

        if (package.UnmaintainedCriticalTransitiveDependencyCount is > 0)
        {
            int count = package.UnmaintainedCriticalTransitiveDependencyCount.Value;
            double criticalTransitiveRisk = Math.Min(1.0, count * 0.5);
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Unmaintained critical transitive dependencies were detected ({count})", criticalTransitiveRisk);
        }

        return risk;
    }

    private static double EvaluateSupplyChainRisk(PackageInfo package, List<string> rationale)
    {
        double risk = 0;

        if (package.IsPackageSigned is false)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "Package signature is missing or invalid", 0.5);
        }
        else if (package.IsPackageSigned is true)
        {
            if (package.HasTrustedPackageSignature is false)
            {
                risk += RiskEvaluationHelpers.AddContribution(rationale, "Package is signed but trust verification failed", 1.0);
            }
            else
            {
                rationale.Add(RiskEvaluationHelpers.CreateRationale("Package is signed and trust verification succeeded", 0.0));
            }
        }

        if (package.HasVerifiedPublisher is false)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                "No trusted NuGet package signature or organization-owned repository was detected", 0.5);
        }
        else if (package.HasVerifiedPublisher is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                "A trusted NuGet package signature or organization-owned repository was detected", 0.0));
        }

        if (package.HasNativeBinaryAssets is true)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                "Package contains native or binary assets that may increase supply-chain exposure", 0.5);
        }

        if (package.VerifiedCommitRatio is < 0.5)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Verified commit coverage is limited ({RiskEvaluationHelpers.FormatPercentage(package.VerifiedCommitRatio.Value)})",
                0.5);
        }
        else if (package.VerifiedCommitRatio != null)
        {
            double verifiedCommitRatio = package.VerifiedCommitRatio.Value;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Verified commit coverage looks healthy ({RiskEvaluationHelpers.FormatPercentage(verifiedCommitRatio)})", 0.0));
        }

        risk += EvaluateAttestationRisk(package, rationale);

        return risk;
    }

    private static double EvaluateAttestationRisk(PackageInfo package, List<string> rationale)
    {
        double risk = 0;

        if (package.HasDetailedSecurityPolicy is false)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "SECURITY policy lacks detailed reporting guidance", 0.25);
        }

        if (package.HasCoordinatedDisclosure is false)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "No coordinated disclosure signal was detected", 0.25);
        }

        if (package.HasProvenanceAttestation is false)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                "No provenance or attestation workflow signal was detected", 0.5);
        }
        else if (package.HasProvenanceAttestation is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Provenance or attestation workflow signal was detected", 0.0));
        }

        if (package.HasReproducibleBuildSignal is false)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                "No reproducible-build or deterministic-build signal was detected", 0.25);
        }

        if (package.HasVerifiedReleaseSignature is false)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "Verified release signature signal was not detected", 0.25);
        }
        else if (package.HasVerifiedReleaseSignature is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Verified release signature signal was detected", 0.0));
        }

        return risk;
    }

    private static double EvaluateOwnershipRisk(PackageInfo package, List<string> rationale)
    {
        double risk = 0;

        if (package.IsDeprecated is true)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "The package version is marked as deprecated", 0.75);
        }

        if (package.OwnerCreatedAt != null &&
            package.OwnerCreatedAt.Value > DateTimeOffset.UtcNow.AddYears(-1))
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "Repository owner account is less than one year old", 0.5);
        }

        if (!package.OwnerIsOrganization && (package.RecentMaintainerCount ?? package.ContributorCount) is <= 1)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "Single maintainer on a non-organization account", 0.5);
        }

        return risk;
    }
}
