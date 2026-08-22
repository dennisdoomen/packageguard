using PackageGuard.Core.Package;

namespace PackageGuard.Core.Risk.Scoring;

/// <summary>
/// Evaluates one risk dimension for a <see cref="PackageInfo"/> and returns the score with supporting rationale.
/// </summary>
internal interface IEvaluateRiskDimension
{
    /// <summary>
    /// Evaluates the risk dimension for the given package and returns the result.
    /// </summary>
    RiskDimensionEvaluation Evaluate(PackageInfo package);
}
