namespace PackageGuard.Core;

/// <summary>
/// Holds the uncapped partial risk score and supporting rationale for a single risk factor
/// within a larger risk dimension evaluation.
/// </summary>
internal sealed record RiskFactorContribution(double Risk, string[] Rationale);
