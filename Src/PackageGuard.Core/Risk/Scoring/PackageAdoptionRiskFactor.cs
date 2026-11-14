using PackageGuard.Core.Package;

namespace PackageGuard.Core.Risk.Scoring;

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

        risk += EvaluateDownloadRisk(package, rationale);
        risk += EvaluateVersionRisk(package, rationale);
        risk += EvaluateEcosystemRisk(package, rationale);

        return new RiskFactorContribution(risk, rationale.ToArray());
    }

    private static double EvaluateDownloadRisk(PackageInfo package, List<string> rationale)
    {
        double risk = 0;

        if (package.DownloadCount is < 1000)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Low package popularity ({package.DownloadCount} downloads)", 2.0);
        }
        else if (package.DownloadCount is < 10000)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Limited package popularity ({package.DownloadCount} downloads)", 1.0);
        }
        else if (package.DownloadCount != null)
        {
            long downloadCount = package.DownloadCount.Value;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Package popularity is established ({downloadCount} downloads)",
                0.0));
        }

        if (package.HasPreOneZeroDependencies)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "Depends on pre-1.0 packages", 0.5);
        }

        if (package.StaleTransitiveDependencyCount is > 0)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Stale transitive dependencies were detected ({package.StaleTransitiveDependencyCount})", 0.25);
        }

        if (package.DeprecatedTransitiveDependencyCount is > 0)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Deprecated transitive dependencies were detected ({package.DeprecatedTransitiveDependencyCount})", 0.5);
        }

        return risk;
    }

    private static double EvaluateVersionRisk(PackageInfo package, List<string> rationale)
    {
        double risk = 0;

        if (package.IsDeprecated is true)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "The package version is marked as deprecated", 1.0);
        }

        if (package.IsMajorVersionBehindLatest)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Current package version is behind latest stable ({package.LatestStableVersion})", 1.5);
        }
        else if (package.IsMinorVersionBehindLatest)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Current package version is behind latest stable ({package.LatestStableVersion})", 0.5);
        }

        if (package.VersionUpdateLagDays is > 365)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"The current version trails the latest stable release by a long time ({RiskEvaluationHelpers.FormatScore(package.VersionUpdateLagDays.Value)} days)",
                1.0);
        }
        else if (package.VersionUpdateLagDays is > 90)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"The current version trails the latest stable release ({RiskEvaluationHelpers.FormatScore(package.VersionUpdateLagDays.Value)} days)",
                0.5);
        }

        return risk;
    }

    private static double EvaluateEcosystemRisk(PackageInfo package, List<string> rationale)
    {
        double risk = 0;

        if (package is { HasModernTargetFrameworkSupport: false, SupportedTargetFrameworks: [_, ..] })
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Target frameworks look dated ({string.Join(", ", package.SupportedTargetFrameworks)})", 0.5);
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
                risk += RiskEvaluationHelpers.AddContribution(rationale,
                    $"OpenSSF Scorecard score is low ({RiskEvaluationHelpers.FormatScore(openSsfScore)})", 1.5);
            }
            else if (openSsfScore < 7.0)
            {
                risk += RiskEvaluationHelpers.AddContribution(rationale,
                    $"OpenSSF Scorecard score is moderate ({RiskEvaluationHelpers.FormatScore(openSsfScore)})", 0.5);
            }
            else
            {
                rationale.Add(RiskEvaluationHelpers.CreateRationale(
                    $"OpenSSF Scorecard score is strong ({RiskEvaluationHelpers.FormatScore(openSsfScore)})", 0.0));
            }
        }

        if (package.HasBranchProtection is false)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "Default branch protection was not detected", 0.5);
        }
        else if (package.HasBranchProtection is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Default branch protection was detected", 0.0));
        }

        if (package.HasRepositoryOwnershipOrRenameChurn is true)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "Repository ownership or rename churn was detected", 0.5);
        }

        return risk;
    }
}
