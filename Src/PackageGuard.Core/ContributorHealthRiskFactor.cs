namespace PackageGuard.Core;

/// <summary>
/// Evaluates contributor and maintainer health signals (count, activity, concentration, diversity).
/// </summary>
internal sealed class ContributorHealthRiskFactor : IEvaluateRiskFactor
{
    /// <inheritdoc/>
    public RiskFactorContribution Evaluate(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (package.ContributorCount is < 2)
        {
            risk += 2.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Low contributor count ({package.ContributorCount})", 2.5));
        }
        else if (package.ContributorCount is < 5)
        {
            risk += 1.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Low contributor count ({package.ContributorCount})", 1.5));
        }
        else if (package.ContributorCount != null)
        {
            int contributorCount = package.ContributorCount.Value;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Contributor count is healthy ({contributorCount})", 0.0));
        }

        if (package.RecentMaintainerCount is < 2)
        {
            risk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Very few active maintainers in the last 6 months ({package.RecentMaintainerCount})", 1.0));
        }
        else if (package.RecentMaintainerCount is < 4)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Limited active maintainer pool in the last 6 months ({package.RecentMaintainerCount})", 0.5));
        }

        if (package.MedianMaintainerActivityDays is > 180)
        {
            risk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Median maintainer inactivity is high ({RiskEvaluationHelpers.FormatScore(package.MedianMaintainerActivityDays.Value)} days since last activity)",
                1.0));
        }
        else if (package.MedianMaintainerActivityDays is > 90)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Median maintainer inactivity is elevated ({RiskEvaluationHelpers.FormatScore(package.MedianMaintainerActivityDays.Value)} days since last activity)",
                0.5));
        }

        if (package.TopContributorShare is >= 0.8)
        {
            risk += 1.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Contribution concentration is high (top contributor owns {RiskEvaluationHelpers.FormatPercentage(package.TopContributorShare.Value)})",
                1.5));
        }
        else if (package.TopTwoContributorShare is >= 0.95)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Contribution concentration is high (top two contributors own {RiskEvaluationHelpers.FormatPercentage(package.TopTwoContributorShare.Value)})",
                0.5));
        }

        if (package is { ExternalContributionRate: <= 0.05, ContributorCount: > 5 })
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"External contribution rate is low ({RiskEvaluationHelpers.FormatPercentage(package.ExternalContributionRate!.Value)})",
                0.25));
        }
        else if (package.ExternalContributionRate is > 0.15)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"External contribution rate looks healthy ({RiskEvaluationHelpers.FormatPercentage(package.ExternalContributionRate.Value)})",
                0.0));
        }

        if (package is { UniqueReviewerCount: < 2, ContributorCount: > 3 })
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Reviewer diversity looks limited ({package.UniqueReviewerCount!.Value} unique reviewers)",
                0.25));
        }
        else if (package.UniqueReviewerCount is > 1)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Reviewer diversity looks healthy ({package.UniqueReviewerCount} unique reviewers)",
                0.0));
        }

        return new RiskFactorContribution(risk, rationale.ToArray());
    }
}
