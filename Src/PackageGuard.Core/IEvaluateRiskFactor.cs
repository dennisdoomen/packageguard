namespace PackageGuard.Core;

/// <summary>
/// Evaluates a single risk factor within a risk dimension and returns an uncapped partial contribution.
/// </summary>
internal interface IEvaluateRiskFactor
{
    /// <summary>
    /// Evaluates the risk factor for the given package and returns the uncapped partial contribution.
    /// </summary>
    RiskFactorContribution Evaluate(PackageInfo package);
}
