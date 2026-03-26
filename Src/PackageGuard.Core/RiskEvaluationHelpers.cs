using System.Globalization;

namespace PackageGuard.Core;

/// <summary>
/// Shared formatting and evaluation helpers used by <see cref="IEvaluateRiskDimension"/> implementations.
/// </summary>
internal static class RiskEvaluationHelpers
{
    /// <summary>
    /// Caps the raw risk score at 10 and returns a <see cref="RiskDimensionEvaluation"/> with the capped score and rationale.
    /// </summary>
    internal static RiskDimensionEvaluation CreateEvaluation(double risk, List<string> rationale)
    {
        double cappedRisk = Math.Min(risk, 10.0);

        if (rationale.Count == 0)
        {
            rationale.Add(CreateRationale("No elevated signals detected", 0.0));
        }

        if (risk > cappedRisk)
        {
            rationale.Add($"Dimension score capped at {FormatScore(cappedRisk)}/10");
        }

        return new RiskDimensionEvaluation(cappedRisk, rationale.ToArray());
    }

    /// <summary>
    /// Formats a rationale entry as "description (+score)".
    /// </summary>
    internal static string CreateRationale(string description, double contribution) =>
        $"{description} (+{FormatScore(contribution)})";

    /// <summary>
    /// Formats a numeric score to one decimal place using invariant culture.
    /// </summary>
    internal static string FormatScore(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a decimal value as a percentage string using invariant culture.
    /// </summary>
    internal static string FormatPercentage(double value) =>
        value.ToString("P0", CultureInfo.InvariantCulture);
}
