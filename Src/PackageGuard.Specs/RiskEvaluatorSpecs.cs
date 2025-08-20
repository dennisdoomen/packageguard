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
}