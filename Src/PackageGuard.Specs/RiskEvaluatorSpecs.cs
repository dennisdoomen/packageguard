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
        package.RiskDimensions.LegalRiskRationale.Should().Contain([
            "Permissive license (MIT) (+0.0)",
            "Missing or invalid license URL (+1.0)",
            "License is incompatible with configured policy (+3.0)"
        ]);
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
        package.RiskDimensions.SecurityRiskRationale.Should().Contain([
            "Public repository available (+0.0)",
            "Known vulnerabilities found (1, max severity 8.0) (+4.0)",
            "Package has a recent vulnerability fix window (<90 days) (+1.0)",
            "Deep dependency chain (depth 11) (+2.0)",
            "Vulnerable transitive dependencies (2) (+1.0)"
        ]);
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
        package.RiskDimensions.OperationalRiskRationale.Should().Contain([
            "Last release is older than 24 months (+4.0)",
            "README is missing or appears to be boilerplate (+1.0)",
            "CONTRIBUTING guide is missing (+1.0)",
            "SECURITY policy is missing (+1.0)",
            "Low contributor count (1) (+3.0)",
            "High number of open bug issues (30) (+2.0)",
            "Stale critical bug issues remain open (1) (+2.0)",
            "Median issue response time is slow (45.0 days) (+2.0)",
            "Median pull request merge time is slow (75.0 days) (+1.0)",
            "Low package popularity (500 downloads) (+3.0)",
            "Depends on pre-1.0 packages (+1.0)",
            "Dimension score capped at 10.0/10"
        ]);
    }

    [TestMethod]
    public void Should_add_new_security_and_operational_signals()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            VulnerabilityCount = 1,
            MaxVulnerabilitySeverity = 6.0,
            HasAvailableSecurityFix = true,
            IsPackageSigned = true,
            HasTrustedPackageSignature = false,
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = false,
            ContributorCount = 10,
            TopContributorShare = 0.85,
            DownloadCount = 50000,
            LatestStableVersion = "2.0.0",
            IsMajorVersionBehindLatest = true,
            HasModernTargetFrameworkSupport = false,
            SupportedTargetFrameworks = ["net472"],
            RecentFailedWorkflowCount = 4,
            HasRecentSuccessfulWorkflowRun = false,
            OpenSsfScore = 4.5,
            HasBranchProtection = false,
            HasProvenanceAttestation = false,
            HasRepositoryOwnershipOrRenameChurn = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.SecurityRisk.Should().Be(5.0);
        package.RiskDimensions.SecurityRiskRationale.Should().Contain([
            "Known vulnerabilities found (1, max severity 6.0) (+3.0)",
            "A security fix is available for a known vulnerability (+1.0)",
            "Package is signed but trust verification failed (+1.0)"
        ]);

        package.RiskDimensions.OperationalRisk.Should().Be(10.0);
        package.RiskDimensions.OperationalRiskRationale.Should().Contain([
            "CHANGELOG or release notes are missing or low quality (+1.0)",
            "Contribution concentration is high (top contributor owns 85 %) (+2.0)",
            "Recent CI workflow failures are elevated (4) (+1.0)",
            "No recent successful CI workflow run detected (+2.0)",
            "Current package version is behind latest stable (2.0.0) (+2.0)",
            "Target frameworks look dated (net472) (+1.0)",
            "OpenSSF Scorecard score is low (4.5) (+2.0)",
            "Default branch protection was not detected (+1.0)",
            "No provenance or attestation workflow signal was detected (+1.0)",
            "Repository ownership or rename churn was detected (+1.0)",
            "Dimension score capped at 10.0/10"
        ]);
    }
}
