namespace PackageGuard.Core;

/// <summary>
/// Evaluates issue and bug health signals (open counts, closure/reopen rates, response times, triage).
/// </summary>
internal sealed class IssueHealthRiskFactor : IEvaluateRiskFactor
{
    /// <inheritdoc/>
    public RiskFactorContribution Evaluate(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (package.OpenBugIssueCount > 25)
        {
            risk += 1.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"High number of open bug issues ({package.OpenBugIssueCount})", 1.5));
        }
        else if (package.OpenBugIssueCount > 10)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Elevated number of open bug issues ({package.OpenBugIssueCount})", 0.5));
        }

        if (package.StaleCriticalBugIssueCount > 0)
        {
            risk += 1.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
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
                rationale.Add(RiskEvaluationHelpers.CreateRationale($"Bug closure rate is low ({RiskEvaluationHelpers.FormatPercentage(bugClosureRate)})", 1.0));
            }
            else if (bugClosureRate < 0.60)
            {
                risk += 0.5;
                rationale.Add(RiskEvaluationHelpers.CreateRationale($"Bug closure rate is moderate ({RiskEvaluationHelpers.FormatPercentage(bugClosureRate)})", 0.5));
            }
            else
            {
                rationale.Add(RiskEvaluationHelpers.CreateRationale($"Bug closure rate looks healthy ({RiskEvaluationHelpers.FormatPercentage(bugClosureRate)})", 0.0));
            }
        }

        double? reopenRate = GetBugReopenRate(package);
        if (reopenRate > 0.20)
        {
            risk += 0.75;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Bug reopen rate is elevated ({RiskEvaluationHelpers.FormatPercentage(reopenRate.Value)})", 0.75));
        }

        if (package.MedianIssueResponseDays > 30)
        {
            risk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Median issue response time is slow ({RiskEvaluationHelpers.FormatScore(package.MedianIssueResponseDays!.Value)} days)",
                1.0));
        }
        else if (package.MedianIssueResponseDays != null)
        {
            double responseDays = package.MedianIssueResponseDays.Value;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Median issue response time looks healthy ({RiskEvaluationHelpers.FormatScore(responseDays)} days)", 0.0));
        }

        if (package.MedianCriticalIssueResponseDays > 7)
        {
            risk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Critical issue response time is slow ({RiskEvaluationHelpers.FormatScore(package.MedianCriticalIssueResponseDays!.Value)} days)",
                1.0));
        }

        if (package.IssueResponseCoverage is < 0.5)
        {
            risk += 0.75;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Maintainer response coverage is low ({RiskEvaluationHelpers.FormatPercentage(package.IssueResponseCoverage.Value)})",
                0.75));
        }

        if (package.IssueTriageWithinSevenDaysRate is < 0.5)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Issue triage within 7 days is low ({RiskEvaluationHelpers.FormatPercentage(package.IssueTriageWithinSevenDaysRate.Value)})",
                0.5));
        }

        if (package.MedianOpenBugAgeDays > 180)
        {
            risk += 0.75;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Median age of open bugs is high ({RiskEvaluationHelpers.FormatScore(package.MedianOpenBugAgeDays!.Value)} days)",
                0.75));
        }

        return new RiskFactorContribution(risk, rationale.ToArray());
    }

    /// <summary>
    /// Computes the ratio of closed to total bug issues in the last 90 days, or <see langword="null"/> if data is unavailable.
    /// </summary>
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

    /// <summary>
    /// Computes the ratio of reopened to closed bug issues, or <see langword="null"/> if data is unavailable.
    /// </summary>
    private static double? GetBugReopenRate(PackageInfo package)
    {
        if (package.ClosedBugIssueCountLast90Days is not > 0 || package.ReopenedBugIssueCountLast90Days is null)
        {
            return null;
        }

        return package.ReopenedBugIssueCountLast90Days.Value / (double)package.ClosedBugIssueCountLast90Days.Value;
    }
}
