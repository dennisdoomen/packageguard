using System.Globalization;

namespace PackageGuard;

/// <summary>
/// Shared console formatting for package risk scores, used by both the <c>analyze</c> and <c>explain</c> commands
/// so risk colors, zone labels, and number formatting stay identical across both.
/// </summary>
internal static class RiskDisplay
{
    /// <summary>
    /// Maps a 0–100 risk score to an Ansi console color name for display.
    /// </summary>
    public static string GetRiskColor(double score)
    {
        return score switch
        {
            >= 60 => "red1",
            >= 30 => "yellow1",
            _ => "green3_1"
        };
    }

    /// <summary>
    /// Maps a 0–100 risk score to a risk zone label: Low, Medium, or High.
    /// </summary>
    public static string GetRiskZone(double score)
    {
        return score switch
        {
            >= 60 => "High",
            >= 30 => "Medium",
            _ => "Low"
        };
    }

    /// <summary>
    /// Formats a double value to one decimal place using invariant culture.
    /// </summary>
    public static string FormatDecimal(double value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
