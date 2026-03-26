namespace PackageGuard.Core;

/// <summary>
/// Evaluates package adoption and supply-chain health signals (downloads, version lag, transitive dependencies,
/// framework support, OpenSSF score, branch protection).
/// </summary>
internal sealed class PackageAdoptionRiskFactor : IEvaluateRiskFactor
{
    /// <inheritdoc/>
    public RiskFactorContribution Evaluate(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (package.DownloadCount is < 1000)
        {
            risk += 2.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Low package popularity ({package.DownloadCount} downloads)", 2.0));
        }
        else if (package.DownloadCount is < 10000)
        {
            risk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Limited package popularity ({package.DownloadCount} downloads)", 1.0));
        }
        else if (package.DownloadCount != null)
        {
            long downloadCount = package.DownloadCount.Value;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Package popularity is established ({downloadCount} downloads)", 0.0));
        }

        if (package.HasPreOneZeroDependencies)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Depends on pre-1.0 packages", 0.5));
        }

        if (package.StaleTransitiveDependencyCount is > 0)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Stale transitive dependencies were detected ({package.StaleTransitiveDependencyCount})", 0.25));
        }

        if (package.AbandonedTransitiveDependencyCount is > 0)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Potentially abandoned transitive dependencies were detected ({package.AbandonedTransitiveDependencyCount})",
                0.5));
        }

        if (package.DeprecatedTransitiveDependencyCount is > 0)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Deprecated transitive dependencies were detected ({package.DeprecatedTransitiveDependencyCount})",
                0.5));
        }

        if (package.UnmaintainedCriticalTransitiveDependencyCount is > 0)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Unmaintained critical transitive dependencies were detected ({package.UnmaintainedCriticalTransitiveDependencyCount})",
                0.5));
        }

        if (package.IsDeprecated is true)
        {
            risk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("The package version is marked as deprecated", 1.0));
        }

        if (package.IsMajorVersionBehindLatest)
        {
            risk += 1.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Current package version is behind latest stable ({package.LatestStableVersion})", 1.5));
        }
        else if (package.IsMinorVersionBehindLatest)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Current package version is behind latest stable ({package.LatestStableVersion})", 0.5));
        }

        if (package is { HasModernTargetFrameworkSupport: false, SupportedTargetFrameworks: [_, ..] })
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Target frameworks look dated ({string.Join(", ", package.SupportedTargetFrameworks)})",
                0.5));
        }
        else if (package.HasModernTargetFrameworkSupport is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Target frameworks include modern runtimes", 0.0));
        }

        if (package.OpenSsfScore != null)
        {
            double openSsfScore = package.OpenSsfScore.Value;
            if (openSsfScore < 5.0)
            {
                risk += 1.5;
                rationale.Add(RiskEvaluationHelpers.CreateRationale($"OpenSSF Scorecard score is low ({RiskEvaluationHelpers.FormatScore(openSsfScore)})", 1.5));
            }
            else if (openSsfScore < 7.0)
            {
                risk += 0.5;
                rationale.Add(RiskEvaluationHelpers.CreateRationale($"OpenSSF Scorecard score is moderate ({RiskEvaluationHelpers.FormatScore(openSsfScore)})", 0.5));
            }
            else
            {
                rationale.Add(RiskEvaluationHelpers.CreateRationale($"OpenSSF Scorecard score is strong ({RiskEvaluationHelpers.FormatScore(openSsfScore)})", 0.0));
            }
        }

        if (package.HasBranchProtection is false)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Default branch protection was not detected", 0.5));
        }
        else if (package.HasBranchProtection is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Default branch protection was detected", 0.0));
        }

        if (package.HasRepositoryOwnershipOrRenameChurn is true)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Repository ownership or rename churn was detected", 0.5));
        }

        if (package.HasVerifiedReleaseSignature is false)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Verified release signature signal was not detected", 0.25));
        }

        if (package.HasVerifiedPublisher is false)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Verified publisher signal was not detected", 0.25));
        }

        if (package.HasReproducibleBuildSignal is false)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No reproducible-build signal was detected", 0.25));
        }

        if (package.VersionUpdateLagDays is > 365)
        {
            risk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"The current version trails the latest stable release by a long time ({RiskEvaluationHelpers.FormatScore(package.VersionUpdateLagDays.Value)} days)",
                1.0));
        }
        else if (package.VersionUpdateLagDays is > 90)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"The current version trails the latest stable release ({RiskEvaluationHelpers.FormatScore(package.VersionUpdateLagDays.Value)} days)",
                0.5));
        }

        return new RiskFactorContribution(risk, rationale.ToArray());
    }
}
