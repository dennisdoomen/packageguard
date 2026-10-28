using PackageGuard.Core.Package;

namespace PackageGuard.Core.Risk.Scoring;

/// <summary>
/// Evaluates operational risk by aggregating all registered risk factor contributions.
/// </summary>
internal sealed class OperationalRiskEvaluator : IEvaluateRiskDimension
{
    private static readonly IEvaluateRiskFactor[] Factors =
    [
        new ReleaseHealthRiskFactor(),
        new DocumentationRiskFactor(),
        new ContributorHealthRiskFactor(),
        new IssueHealthRiskFactor(),
        new CiHealthRiskFactor(),
        new PackageAdoptionRiskFactor()
    ];

    /// <inheritdoc/>
    public RiskDimensionEvaluation Evaluate(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (package.HasIncompleteGitHubRiskData)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale(
                "Inconclusive: some GitHub repository data could not be retrieved, so signals may be missing", 0.0));
        }

        foreach (IEvaluateRiskFactor factor in Factors)
        {
            RiskFactorContribution contribution = factor.Evaluate(package);
            risk += contribution.Risk;
            rationale.AddRange(contribution.Rationale);
        }

        return RiskEvaluationHelpers.CreateEvaluation(risk, rationale);
    }
}
