using PackageGuard.Core.Package;

namespace PackageGuard.Core.Risk.Scoring;

/// <summary>
/// Evaluates documentation quality signals (README, CONTRIBUTING guide, SECURITY policy, CHANGELOG).
/// </summary>
/// <remarks>
/// A signal that is unknown only because GitHub did not answer is reported as undetermined instead of missing, so an
/// exhausted rate limit does not make a well-documented package look risky.
/// </remarks>
internal sealed class DocumentationRiskFactor : IEvaluateRiskFactor
{
    /// <inheritdoc/>
    public RiskFactorContribution Evaluate(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();
        bool isIncomplete = package.HasIncompleteGitHubRiskData;

        if (isIncomplete && package.HasReadme is null)
        {
            rationale.Add(CreateUndeterminedRationale("whether a README exists"));
        }
        else if (package.HasReadme is not true || package.HasDefaultReadme is true)
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

        if (isIncomplete && package.HasContributingGuide is null)
        {
            rationale.Add(CreateUndeterminedRationale("whether a CONTRIBUTING guide exists"));
        }
        else if (package.HasContributingGuide != true)
        {
            risk += 0.5;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("CONTRIBUTING guide is missing", 0.5));
        }
        else
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("CONTRIBUTING guide is present", 0.0));
        }

        if (isIncomplete && package.HasSecurityPolicy is null)
        {
            rationale.Add(CreateUndeterminedRationale("whether a SECURITY policy exists"));
        }
        else if (package.HasSecurityPolicy != true)
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

        if (!hasAcceptableReleaseHistory && isIncomplete && IsReleaseHistoryUnknown(package))
        {
            rationale.Add(CreateUndeterminedRationale("whether a CHANGELOG or release notes exist"));
        }
        else if (!hasAcceptableReleaseHistory)
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

    /// <summary>
    /// Returns <see langword="true"/> when the release notes, the CHANGELOG, or the quality of that CHANGELOG could not
    /// be determined.
    /// </summary>
    private static bool IsReleaseHistoryUnknown(PackageInfo package) =>
        package is { HasReleaseNotes: null } or { HasChangelog: null } or { HasChangelog: true, HasDefaultChangelog: null };

    /// <summary>
    /// Creates the rationale for a signal that GitHub did not provide, which therefore does not add any risk.
    /// </summary>
    private static string CreateUndeterminedRationale(string subject) =>
        RiskEvaluationHelpers.CreateRationale($"Could not determine {subject} because GitHub data is incomplete", 0.0);
}
