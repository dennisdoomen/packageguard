namespace PackageGuard.Core;

/// <summary>
/// Evaluates documentation quality signals (README, CONTRIBUTING guide, SECURITY policy, CHANGELOG).
/// </summary>
internal sealed class DocumentationRiskFactor : IEvaluateRiskFactor
{
    /// <inheritdoc/>
    public RiskFactorContribution Evaluate(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (package.HasReadme is not true || package.HasDefaultReadme is true)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("README is missing or appears to be boilerplate", 0.5));
        }
        else
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("README looks present and non-default", 0.0));
        }

        if (package.ReadmeUpdatedAt != null &&
            package.ReadmeUpdatedAt.Value < DateTimeOffset.UtcNow.AddMonths(-18))
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("README has not been refreshed recently", 0.25));
        }

        if (package.HasContributingGuide != true)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("CONTRIBUTING guide is missing", 0.5));
        }
        else
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("CONTRIBUTING guide is present", 0.0));
        }

        if (package.HasSecurityPolicy != true)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("SECURITY policy is missing", 0.5));
        }
        else
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("SECURITY policy is present", 0.0));
        }

        if (package.HasDetailedSecurityPolicy is false)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("SECURITY policy lacks concrete response instructions", 0.25));
        }

        bool hasAcceptableReleaseHistory = package.HasReleaseNotes is true ||
                                           (package.HasChangelog is true && package.HasDefaultChangelog is not true);
        if (!hasAcceptableReleaseHistory)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("CHANGELOG or release notes are missing or low quality", 0.5));
        }
        else
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("CHANGELOG or release notes are present", 0.0));
        }

        if (package.ChangelogUpdatedAt != null &&
            package.ChangelogUpdatedAt.Value < DateTimeOffset.UtcNow.AddMonths(-18))
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("CHANGELOG has not been refreshed recently", 0.25));
        }

        return new RiskFactorContribution(risk, rationale.ToArray());
    }
}
