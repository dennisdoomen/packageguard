namespace PackageGuard.Core;

/// <summary>
/// Evaluates release cadence and versioning health signals.
/// </summary>
internal sealed class ReleaseHealthRiskFactor : IEvaluateRiskFactor
{
    /// <inheritdoc/>
    public RiskFactorContribution Evaluate(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (package.PublishedAt != null)
        {
            DateTimeOffset publishedAt = package.PublishedAt.Value;
            if (publishedAt < DateTimeOffset.UtcNow.AddMonths(-24))
            {
                risk += 2.0;
                rationale.Add(RiskEvaluationHelpers.CreateRationale("Last release is older than 24 months", 2.0));
            }
            else if (publishedAt < DateTimeOffset.UtcNow.AddMonths(-12))
            {
                risk += 1.0;
                rationale.Add(RiskEvaluationHelpers.CreateRationale("Last release is older than 12 months", 1.0));
            }
            else
            {
                rationale.Add(RiskEvaluationHelpers.CreateRationale("Release cadence looks current", 0.0));
            }
        }

        if (package.PrereleaseRatio is > 0.5)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"High prerelease ratio detected ({RiskEvaluationHelpers.FormatPercentage(package.PrereleaseRatio.Value)})",
                0.5));
        }

        if (package.RapidReleaseCorrectionCount is > 1)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Rapid release corrections were detected ({package.RapidReleaseCorrectionCount})",
                0.5));
        }

        if (package.MeanReleaseIntervalDays is > 365)
        {
            risk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Mean release interval is long ({RiskEvaluationHelpers.FormatScore(package.MeanReleaseIntervalDays.Value)} days)",
                1.0));
        }
        else if (package.MeanReleaseIntervalDays is > 180)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"Mean release interval is elevated ({RiskEvaluationHelpers.FormatScore(package.MeanReleaseIntervalDays.Value)} days)",
                0.5));
        }

        if (package.HasSemVerReleaseTags is false)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Recent release tags do not consistently follow semantic versioning", 0.5));
        }
        else if (package.HasSemVerReleaseTags is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Recent release tags follow semantic versioning", 0.0));
        }

        if (package.MajorReleaseRatio is > 0.40)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                $"A high share of semver release transitions were major-version jumps ({RiskEvaluationHelpers.FormatPercentage(package.MajorReleaseRatio.Value)})",
                0.5));
        }

        return new RiskFactorContribution(risk, rationale.ToArray());
    }
}
