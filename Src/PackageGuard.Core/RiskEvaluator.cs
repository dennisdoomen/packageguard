using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

/// <summary>
/// Evaluates and calculates risk metrics for NuGet packages.
/// </summary>
public class RiskEvaluator(ILogger logger)
{
    /// <summary>
    /// Evaluates the risk for a given package and updates its risk properties.
    /// </summary>
    /// <param name="package">The package to evaluate.</param>
    public void EvaluateRisk(PackageInfo package)
    {
        logger.LogDebug("Evaluating risk for package {PackageName} {Version}", package.Name, package.Version);

        var riskDimensions = new RiskDimensions
        {
            LegalRisk = EvaluateLegalRisk(package),
            SecurityRisk = EvaluateSecurityRisk(package),
            OperationalRisk = EvaluateOperationalRisk(package)
        };

        package.RiskDimensions = riskDimensions;
        package.RiskScore = riskDimensions.OverallRisk * 10; // Scale to 0-100

        logger.LogDebug("Risk evaluation complete for {PackageName}: Overall={OverallRisk:F1}, Legal={LegalRisk:F1}, Security={SecurityRisk:F1}, Operational={OperationalRisk:F1}",
            package.Name, package.RiskScore, riskDimensions.LegalRisk, riskDimensions.SecurityRisk, riskDimensions.OperationalRisk);
    }

    /// <summary>
    /// Evaluates legal risk based on license information and compliance factors.
    /// </summary>
    private double EvaluateLegalRisk(PackageInfo package)
    {
        var risk = 0.0;

        // License risk assessment
        if (string.IsNullOrEmpty(package.License) || package.License == "Unknown")
        {
            risk += 8.0; // High risk for unknown licenses
        }
        else if (IsRestrictiveLicense(package.License))
        {
            risk += 6.0; // High risk for restrictive licenses
        }
        else if (IsPermissiveLicense(package.License))
        {
            risk += 1.0; // Low risk for permissive licenses
        }
        else
        {
            risk += 4.0; // Medium risk for other licenses
        }

        // Missing license URL adds minor risk
        if (string.IsNullOrEmpty(package.LicenseUrl))
        {
            risk += 1.0;
        }

        return Math.Min(risk, 10.0);
    }

    /// <summary>
    /// Evaluates security risk based on known vulnerabilities and source transparency.
    /// </summary>
    private double EvaluateSecurityRisk(PackageInfo package)
    {
        var risk = 0.0;

        // Source transparency risk
        if (string.IsNullOrEmpty(package.RepositoryUrl))
        {
            risk += 5.0; // Higher risk when source is not available
        }

        // TODO: In a complete implementation, this would:
        // - Query vulnerability databases (CVE, NVD, GitHub Security Advisories)
        // - Check package signing status
        // - Evaluate maintainer reputation
        // - Assess dependency chain depth

        // For now, assign a baseline security risk
        risk += 2.0; // Base security risk

        return Math.Min(risk, 10.0);
    }

    /// <summary>
    /// Evaluates operational risk based on maintenance status and package activity.
    /// </summary>
    private double EvaluateOperationalRisk(PackageInfo package)
    {
        var risk = 0.0;

        // TODO: In a complete implementation, this would:
        // - Check last release date and frequency
        // - Evaluate download popularity
        // - Assess maintainer responsiveness
        // - Check for open critical issues
        // - Evaluate dependency requirements

        // For now, assign a baseline operational risk
        risk += 3.0; // Base operational risk

        return Math.Min(risk, 10.0);
    }

    /// <summary>
    /// Determines if a license is considered restrictive (copyleft, commercial restrictions).
    /// </summary>
    private static bool IsRestrictiveLicense(string license)
    {
        var restrictiveLicenses = new[]
        {
            "GPL", "AGPL", "LGPL", "SSPL", "Commons Clause", "BUSL", "BCL"
        };

        return restrictiveLicenses.Any(restrictive => 
            license.Contains(restrictive, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines if a license is considered permissive (MIT, Apache, BSD).
    /// </summary>
    private static bool IsPermissiveLicense(string license)
    {
        var permissiveLicenses = new[]
        {
            "MIT", "Apache", "BSD", "ISC", "Unlicense", "WTFPL", "CC0"
        };

        return permissiveLicenses.Any(permissive => 
            license.Contains(permissive, StringComparison.OrdinalIgnoreCase));
    }
}