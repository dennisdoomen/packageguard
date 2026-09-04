using System.Globalization;

namespace PackageGuard.Core.Risk.Scoring;

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
            rationale.Add($"Dimension score capped at {FormatTotalScore(cappedRisk)}/10");
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
    /// Formats a total risk score to one decimal place, dropping the decimal entirely when the value is a
    /// round number so e.g. "10.0/10" reads as "10/10".
    /// </summary>
    internal static string FormatTotalScore(double value)
    {
        string formatted = FormatScore(value);
        return formatted.EndsWith(".0", StringComparison.Ordinal) ? formatted[..^2] : formatted;
    }

    /// <summary>
    /// Formats a decimal value as a percentage string using invariant culture.
    /// </summary>
    internal static string FormatPercentage(double value) =>
        value.ToString("P0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a list of human-readable evidence details (e.g. affected package names/versions) as a
    /// rationale suffix, truncating to <paramref name="maxItems"/> entries and appending an "and N more"
    /// marker for the remainder. Returns an empty string when <paramref name="details"/> is empty, so it
    /// can be safely appended to a rationale description regardless of whether detail data is available.
    /// </summary>
    internal static string FormatDetailList(IReadOnlyCollection<string> details, int maxItems = 8)
    {
        if (details.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> shown = details.Take(maxItems);
        int remaining = details.Count - maxItems;

        string joined = remaining > 0
            ? string.Join("; ", shown) + $"; and {remaining} more"
            : string.Join("; ", shown);

        return $": {joined}";
    }
}
