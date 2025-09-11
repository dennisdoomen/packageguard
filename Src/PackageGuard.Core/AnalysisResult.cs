namespace PackageGuard.Core;

/// <summary>
/// Represents the results of a package analysis, including policy violations and package risk information.
/// </summary>
public class AnalysisResult
{
    /// <summary>
    /// Gets or sets the policy violations found during analysis.
    /// </summary>
    public PolicyViolation[] Violations { get; set; } = Array.Empty<PolicyViolation>();

    /// <summary>
    /// Gets or sets all packages analyzed with their risk metrics.
    /// </summary>
    public PackageInfo[] Packages { get; set; } = Array.Empty<PackageInfo>();
}