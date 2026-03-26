namespace PackageGuard;

/// <summary>
/// Holds the file paths for the HTML and SARIF risk reports generated during analysis.
/// </summary>
/// <param name="HtmlPath">The file path of the generated HTML risk report.</param>
/// <param name="SarifPath">The file path of the generated SARIF risk report.</param>
internal sealed record RiskReportPaths(string HtmlPath, string SarifPath);
