using System;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
public class RiskEvaluatorSpecs
{
    [TestMethod]
    public void Should_evaluate_legal_risk_for_unknown_license()
    {
        // Arrange
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "Unknown"
        };

        // Act
        riskEvaluator.EvaluateRisk(package);

        // Assert
        package.RiskDimensions.LegalRisk.Should().BeGreaterThan(6.0); // High risk for unknown license
        package.RiskScore.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void Should_evaluate_legal_risk_for_permissive_license()
    {
        // Arrange
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            LicenseUrl = "https://opensource.org/licenses/MIT"
        };

        // Act
        riskEvaluator.EvaluateRisk(package);

        // Assert
        package.RiskDimensions.LegalRisk.Should().BeLessThan(3.0); // Low risk for MIT license
        package.RiskScore.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void Should_evaluate_legal_risk_for_restrictive_license()
    {
        // Arrange
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "GPL-3.0"
        };

        // Act
        riskEvaluator.EvaluateRisk(package);

        // Assert
        package.RiskDimensions.LegalRisk.Should().BeGreaterThan(5.0); // High risk for GPL license
        package.RiskScore.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void Should_evaluate_security_risk_based_on_repository_url()
    {
        // Arrange
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var packageWithRepo = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package"
        };

        var packageWithoutRepo = new PackageInfo
        {
            Name = "TestPackage2",
            Version = "1.0.0",
            License = "MIT"
        };

        // Act
        riskEvaluator.EvaluateRisk(packageWithRepo);
        riskEvaluator.EvaluateRisk(packageWithoutRepo);

        // Assert
        packageWithoutRepo.RiskDimensions.SecurityRisk.Should().BeGreaterThan(packageWithRepo.RiskDimensions.SecurityRisk);
    }

    [TestMethod]
    public void Should_calculate_overall_risk_score()
    {
        // Arrange
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "Unknown"
        };

        // Act
        riskEvaluator.EvaluateRisk(package);

        // Assert
        var expectedOverallRisk = (package.RiskDimensions.LegalRisk + 
                                   package.RiskDimensions.SecurityRisk + 
                                   package.RiskDimensions.OperationalRisk) / 3.0;
        
        package.RiskDimensions.OverallRisk.Should().Be(expectedOverallRisk);
        package.RiskScore.Should().Be(expectedOverallRisk * 10); // Scaled to 0-100
    }

    [TestMethod]
    public void Should_add_legal_risk_for_invalid_license_url_and_policy_incompatibility()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            HasValidLicenseUrl = false,
            IsLicensePolicyCompatible = false
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.LegalRisk.Should().Be(4.0);
    }

    [TestMethod]
    public void Should_add_security_risk_for_vulnerabilities_and_dependency_depth()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            VulnerabilityCount = 1,
            MaxVulnerabilitySeverity = 8.0,
            HasPatchedVulnerabilityInLast90Days = true,
            DependencyDepth = 11,
            TransitiveVulnerabilityCount = 2
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.SecurityRisk.Should().Be(8.0);
    }

    [TestMethod]
    public void Should_add_operational_risk_for_poor_repository_hygiene_and_low_popularity()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-30),
            HasReadme = false,
            HasContributingGuide = false,
            HasSecurityPolicy = false,
            ContributorCount = 1,
            OpenBugIssueCount = 30,
            StaleCriticalBugIssueCount = 1,
            MedianIssueResponseDays = 45,
            MedianPullRequestMergeDays = 75,
            DownloadCount = 500,
            HasPreOneZeroDependencies = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRisk.Should().Be(10.0);
    }
}
