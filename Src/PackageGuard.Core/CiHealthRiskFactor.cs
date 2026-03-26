namespace PackageGuard.Core;

/// <summary>
/// Evaluates CI and workflow health signals (PR merge time, failure rates, test/coverage signals, branch protection).
/// </summary>
internal sealed class CiHealthRiskFactor : IEvaluateRiskFactor
{
    /// <inheritdoc/>
    public RiskFactorContribution Evaluate(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (package.MedianPullRequestMergeDays > 60)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Median pull request merge time is slow ({RiskEvaluationHelpers.FormatScore(package.MedianPullRequestMergeDays!.Value)} days)",
                0.5));
        }

        if (package.RecentFailedWorkflowCount is >= 3)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Recent CI workflow failures are elevated ({package.RecentFailedWorkflowCount})",
                0.5));
        }

        if (package.WorkflowFailureRate is > 0.5)
        {
            risk += 0.75;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"CI workflow failure rate is elevated ({RiskEvaluationHelpers.FormatPercentage(package.WorkflowFailureRate.Value)})",
                0.75));
        }

        if (package.HasFlakyWorkflowPattern is true)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("CI workflow history shows a potentially flaky failure pattern", 0.5));
        }

        if (package.HasRecentSuccessfulWorkflowRun is false)
        {
            risk += 1.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No recent successful CI workflow run detected", 1.5));
        }
        else if (package.HasRecentSuccessfulWorkflowRun is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Recent successful CI workflow run detected", 0.0));
        }

        if (package.RequiredStatusCheckCount is 0)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No required status checks were detected on the default branch", 0.5));
        }
        else if (package.RequiredStatusCheckCount is > 0)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Required status checks are configured ({package.RequiredStatusCheckCount})", 0.0));
        }

        if (package.WorkflowPlatformCount is < 2)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("CI workflow matrix breadth looks limited", 0.25));
        }

        if (package.HasCoverageWorkflowSignal is false)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No coverage workflow signal was detected", 0.25));
        }

        if (package.HasTestSignal is false)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No explicit test execution signal was detected", 0.5));
        }

        if (package.HasDependencyUpdateAutomation is false)
        {
            risk += 0.25;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No dependency update automation signal was detected", 0.25));
        }

        return new RiskFactorContribution(risk, rationale.ToArray());
    }
}
