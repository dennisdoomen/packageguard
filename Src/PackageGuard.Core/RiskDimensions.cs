using MemoryPack;

namespace PackageGuard.Core;

/// <summary>
/// Represents the individual risk scores across different dimensions for a package.
/// </summary>
[MemoryPackable]
public partial class RiskDimensions
{
    /// <summary>
    /// Legal risk score (0-10): License compliance, ECCN, export control considerations.
    /// </summary>
    public double LegalRisk { get; set; }

    /// <summary>
    /// Security risk score (0-10): Known vulnerabilities, source code transparency.
    /// </summary>
    public double SecurityRisk { get; set; }

    /// <summary>
    /// Operational risk score (0-10): Version activity, maintenance status, known issues.
    /// </summary>
    public double OperationalRisk { get; set; }

    /// <summary>
    /// Gets the overall risk score calculated from individual dimensions.
    /// </summary>
    public double OverallRisk => (LegalRisk + SecurityRisk + OperationalRisk) / 3.0;
}