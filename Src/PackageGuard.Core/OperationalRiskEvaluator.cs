namespace PackageGuard.Core;

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

        foreach (IEvaluateRiskFactor factor in Factors)
        {
            RiskFactorContribution contribution = factor.Evaluate(package);
            risk += contribution.Risk;
            rationale.AddRange(contribution.Rationale);
        }

        return RiskEvaluationHelpers.CreateEvaluation(risk, rationale);
    }
}
