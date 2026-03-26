namespace PackageGuard.Core;

/// <summary>
/// Holds the computed score and rationale strings for a single risk dimension.
/// </summary>
internal sealed record RiskDimensionEvaluation(double Score, string[] Rationale);
