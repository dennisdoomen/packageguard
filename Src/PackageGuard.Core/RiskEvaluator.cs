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

        if (string.IsNullOrEmpty(package.License) || package.License == "Unknown")
        {
            risk += 6.0;
        }
        else if (IsRestrictiveLicense(package.License))
        {
            risk += 6.0;
        }
        else if (IsWeakCopyleftLicense(package.License))
        {
            risk += 3.0;
        }
        else if (IsPermissiveLicense(package.License))
        {
            risk += 0.0;
        }

        if (package.HasValidLicenseUrl == false || string.IsNullOrEmpty(package.LicenseUrl))
        {
            risk += 1.0;
        }

        if (package.IsLicensePolicyCompatible == false)
        {
            risk += 3.0;
        }

        return Math.Min(risk, 10.0);
    }

    /// <summary>
    /// Evaluates security risk based on known vulnerabilities and source transparency.
    /// </summary>
    private double EvaluateSecurityRisk(PackageInfo package)
    {
        var risk = 0.0;

        if (string.IsNullOrEmpty(package.RepositoryUrl))
        {
            risk += 4.0;
        }

        double vulnerabilityRisk = 0;
        if (package.VulnerabilityCount > 0)
        {
            vulnerabilityRisk += Math.Min(4.0, package.MaxVulnerabilitySeverity / 2.0);
        }

        if (package.HasPatchedVulnerabilityInLast90Days)
        {
            vulnerabilityRisk += 1.0;
        }

        risk += Math.Min(6.0, vulnerabilityRisk);

        if (package.DependencyDepth > 20)
        {
            risk += 3.0;
        }
        else if (package.DependencyDepth > 10)
        {
            risk += 2.0;
        }

        risk += Math.Min(2.0, package.TransitiveVulnerabilityCount * 0.5);

        if (package.IsPackageSigned == false)
        {
            risk += 1.0;
        }

        if (package.OwnerCreatedAt is DateTimeOffset ownerCreatedAt &&
            ownerCreatedAt > DateTimeOffset.UtcNow.AddYears(-1))
        {
            risk += 1.0;
        }

        if (!package.OwnerIsOrganization && package.ContributorCount is <= 1)
        {
            risk += 1.0;
        }

        if (package.PublishedAt is DateTimeOffset publishedAt &&
            publishedAt < DateTimeOffset.UtcNow.AddMonths(-24))
        {
            risk += 2.0;
        }

        return Math.Min(risk, 10.0);
    }

    /// <summary>
    /// Evaluates operational risk based on maintenance status and package activity.
    /// </summary>
    private double EvaluateOperationalRisk(PackageInfo package)
    {
        var risk = 0.0;

        if (package.PublishedAt is DateTimeOffset publishedAt)
        {
            if (publishedAt < DateTimeOffset.UtcNow.AddMonths(-24))
            {
                risk += 4.0;
            }
            else if (publishedAt < DateTimeOffset.UtcNow.AddMonths(-12))
            {
                risk += 2.0;
            }
        }

        if (package.HasReadme != true || package.HasDefaultReadme == true)
        {
            risk += 1.0;
        }

        if (package.HasContributingGuide != true)
        {
            risk += 1.0;
        }

        if (package.HasSecurityPolicy != true)
        {
            risk += 1.0;
        }

        if (package.ContributorCount is < 2)
        {
            risk += 3.0;
        }
        else if (package.ContributorCount is < 5)
        {
            risk += 2.0;
        }

        if (package.OpenBugIssueCount > 25)
        {
            risk += 2.0;
        }
        else if (package.OpenBugIssueCount > 10)
        {
            risk += 1.0;
        }

        if (package.StaleCriticalBugIssueCount > 0)
        {
            risk += 2.0;
        }

        if (package.MedianIssueResponseDays > 30)
        {
            risk += 2.0;
        }

        if (package.MedianPullRequestMergeDays > 60)
        {
            risk += 1.0;
        }

        if (package.DownloadCount is < 1000)
        {
            risk += 3.0;
        }
        else if (package.DownloadCount is < 10000)
        {
            risk += 2.0;
        }

        if (package.HasPreOneZeroDependencies)
        {
            risk += 1.0;
        }

        return Math.Min(risk, 10.0);
    }

    /// <summary>
    /// Determines if a license is considered restrictive (copyleft, commercial restrictions).
    /// </summary>
    private static bool IsRestrictiveLicense(string license)
    {
        var restrictiveLicenses = new[]
        {
            "GPL", "AGPL", "SSPL", "Commons Clause", "BUSL", "BCL"
        };

        return restrictiveLicenses.Any(restrictive => 
            license.Contains(restrictive, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWeakCopyleftLicense(string license)
    {
        var weakCopyleftLicenses = new[]
        {
            "LGPL", "MPL"
        };

        return weakCopyleftLicenses.Any(candidate =>
            license.Contains(candidate, StringComparison.OrdinalIgnoreCase));
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
