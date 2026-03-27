using System;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
internal class RiskEvaluatorSpecs
{
    [TestMethod]
    internal void Should_evaluate_legal_risk_for_unknown_license()
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
    internal void Should_evaluate_legal_risk_for_permissive_license()
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
    internal void Should_evaluate_legal_risk_for_restrictive_license()
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
    internal void Should_evaluate_security_risk_based_on_repository_url()
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
    internal void Should_calculate_overall_risk_score()
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
        var expectedOverallRisk = (package.RiskDimensions.LegalRisk * 0.20) +
                                   (package.RiskDimensions.SecurityRisk * 0.45) +
                                   (package.RiskDimensions.OperationalRisk * 0.35);
        
        package.RiskDimensions.OverallRisk.Should().Be(expectedOverallRisk);
        package.RiskScore.Should().Be(expectedOverallRisk * 10); // Scaled to 0-100
    }

    [TestMethod]
    internal void Should_add_legal_risk_for_invalid_license_url_and_policy_incompatibility()
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
    internal void Should_add_security_risk_for_vulnerabilities_and_dependency_depth()
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
            MedianVulnerabilityFixDays = 200,
            DependencyDepth = 11,
            TransitiveVulnerabilityCount = 2,
            StaleTransitiveDependencyCount = 3
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.SecurityRisk.Should().BeGreaterThan(7.0);
        package.RiskDimensions.SecurityRiskRationale.Should().Contain(item => item.Contains("Known vulnerabilities found (1, max severity 8.0)"));
        package.RiskDimensions.SecurityRiskRationale.Should().Contain(item => item.Contains("Median vulnerability fix time is slow"));
        package.RiskDimensions.SecurityRiskRationale.Should().Contain(item => item.Contains("Deep dependency chain (depth 11)"));
        package.RiskDimensions.SecurityRiskRationale.Should().Contain(item => item.Contains("Vulnerable transitive dependencies (2)"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_poor_repository_hygiene_and_low_popularity()
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
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Last release is older than 24 months"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("README is missing or appears to be boilerplate"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("CONTRIBUTING guide is missing"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("SECURITY policy is missing"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Low contributor count (1)"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("High number of open bug issues (30)"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Low package popularity (500 downloads)"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Dimension score capped at 10.0/10"));
    }

    [TestMethod]
    internal void Should_add_new_security_and_operational_signals()
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
            MedianVulnerabilityFixDays = 120,
            IsPackageSigned = true,
            HasTrustedPackageSignature = false,
            HasVerifiedPublisher = false,
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasDetailedSecurityPolicy = false,
            HasChangelog = false,
            ContributorCount = 10,
            RecentMaintainerCount = 2,
            TopContributorShare = 0.85,
            DownloadCount = 50000,
            LatestStableVersion = "2.0.0",
            IsMajorVersionBehindLatest = true,
            HasModernTargetFrameworkSupport = false,
            SupportedTargetFrameworks = ["net472"],
            RecentFailedWorkflowCount = 4,
            HasRecentSuccessfulWorkflowRun = false,
            WorkflowFailureRate = 0.6,
            HasFlakyWorkflowPattern = true,
            RequiredStatusCheckCount = 0,
            WorkflowPlatformCount = 1,
            HasCoverageWorkflowSignal = false,
            OpenSsfScore = 4.5,
            HasBranchProtection = false,
            HasProvenanceAttestation = false,
            HasReproducibleBuildSignal = false,
            HasVerifiedReleaseSignature = false,
            HasRepositoryOwnershipOrRenameChurn = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.SecurityRisk.Should().BeGreaterThan(5.0);
        package.RiskDimensions.SecurityRiskRationale.Should().Contain(item => item.Contains("A security fix is available for a known vulnerability"));
        package.RiskDimensions.SecurityRiskRationale.Should().Contain(item => item.Contains("Package is signed but trust verification failed"));
        package.RiskDimensions.SecurityRiskRationale.Should().Contain(item => item.Contains("Verified publisher signal was not detected"));

        package.RiskDimensions.OperationalRisk.Should().BeGreaterThan(9.0);
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("CHANGELOG or release notes are missing or low quality"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Contribution concentration is high"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Recent CI workflow failures are elevated (4)"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("No recent successful CI workflow run detected"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("No required status checks were detected"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Current package version is behind latest stable (2.0.0)"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("OpenSSF Scorecard score is low (4.5)"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Repository ownership or rename churn was detected"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_issue_closure_and_workflow_quality_signals()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-3),
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true,
            ContributorCount = 6,
            RecentMaintainerCount = 2,
            OpenBugIssueCount = 12,
            ClosedBugIssueCountLast90Days = 2,
            ReopenedBugIssueCountLast90Days = 1,
            MedianCriticalIssueResponseDays = 10,
            IssueResponseCoverage = 0.4,
            MedianOpenBugAgeDays = 220,
            HasRecentSuccessfulWorkflowRun = true,
            WorkflowFailureRate = 0.7,
            HasFlakyWorkflowPattern = true,
            RequiredStatusCheckCount = 0,
            WorkflowPlatformCount = 1,
            HasCoverageWorkflowSignal = false,
            DownloadCount = 20000
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Bug closure rate is low"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Bug reopen rate is elevated"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Critical issue response time is slow"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Maintainer response coverage is low"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("CI workflow failure rate is elevated"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("CI workflow history shows a potentially flaky failure pattern"));
    }

    [TestMethod]
    internal void Should_add_easy_and_medium_quality_metric_signals()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-14),
            LatestStableVersion = "2.0.0",
            LatestStablePublishedAt = DateTimeOffset.UtcNow.AddDays(-5),
            VersionUpdateLagDays = 420,
            IsMajorVersionBehindLatest = true,
            IsDeprecated = true,
            DeprecatedTransitiveDependencyCount = 2,
            UnmaintainedCriticalTransitiveDependencyCount = 1,
            HasVerifiedReleaseSignature = false,
            VerifiedCommitRatio = 0.2,
            MeanReleaseIntervalDays = 400,
            HasReleaseNotes = false,
            HasSemVerReleaseTags = false,
            MajorReleaseRatio = 0.5,
            RecentMaintainerCount = 3,
            MedianMaintainerActivityDays = 150,
            IssueTriageWithinSevenDaysRate = 0.3,
            HasDependencyUpdateAutomation = false,
            HasTestSignal = false,
            ExternalContributionRate = 0.02,
            UniqueReviewerCount = 1,
            ContributorCount = 8,
            DownloadCount = 20000,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true,
            HasCoverageWorkflowSignal = true,
            WorkflowPlatformCount = 2,
            HasRecentSuccessfulWorkflowRun = true,
            OpenSsfScore = 7.5
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.SecurityRiskRationale.Should().Contain(item => item.Contains("Unmaintained critical transitive dependencies were detected"));
        package.RiskDimensions.SecurityRiskRationale.Should().Contain(item => item.Contains("Verified commit coverage is limited"));
        package.RiskDimensions.SecurityRiskRationale.Should().Contain(item => item.Contains("The package version is marked as deprecated"));

        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Mean release interval is long"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Recent release tags do not consistently follow semantic versioning"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("A high share of semver release transitions were major-version jumps (50 %)"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Median maintainer inactivity is elevated"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Issue triage within 7 days is low"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("No dependency update automation signal was detected"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("No explicit test execution signal was detected"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Deprecated transitive dependencies were detected"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("Reviewer diversity looks limited"));
        package.RiskDimensions.OperationalRiskRationale.Should().Contain(item => item.Contains("The current version trails the latest stable release by a long time"));
    }

    [TestMethod]
    internal void Should_not_flag_major_release_ratio_when_releases_stay_on_same_major_line()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            MajorReleaseRatio = 0.0,
            ContributorCount = 8,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .NotContain(item => item.Contains("major-version jumps"));
    }

    [TestMethod]
    internal void Should_treat_release_notes_as_a_valid_changelog_replacement()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            HasReleaseNotes = true,
            HasChangelog = false,
            ContributorCount = 8,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("CHANGELOG or release notes are present"));
        package.RiskDimensions.OperationalRiskRationale.Should()
            .NotContain(item => item.Contains("CHANGELOG or release notes are missing or low quality"));
    }

    [TestMethod]
    internal void Should_evaluate_legal_risk_for_mpl_as_weak_copyleft()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MPL-2.0",
            LicenseUrl = "https://opensource.org/licenses/MPL-2.0",
            HasValidLicenseUrl = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.LegalRisk.Should().Be(3.0);
        package.RiskDimensions.LegalRiskRationale.Should().Contain(item => item.Contains("Weak copyleft license (MPL-2.0)"));
    }

    [TestMethod]
    internal void Should_evaluate_legal_risk_for_agpl_as_restrictive()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "AGPL-3.0",
            LicenseUrl = "https://opensource.org/licenses/AGPL-3.0",
            HasValidLicenseUrl = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.LegalRisk.Should().Be(6.0);
        package.RiskDimensions.LegalRiskRationale.Should().Contain(item => item.Contains("Restrictive license (AGPL-3.0)"));
    }

    [TestMethod]
    internal void Should_add_security_risk_when_package_signature_is_missing()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            IsPackageSigned = false
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.SecurityRiskRationale.Should()
            .Contain(item => item.Contains("Package signature is missing or invalid"));
    }

    [TestMethod]
    internal void Should_apply_higher_security_risk_for_dependency_chain_deeper_than_20()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var deepPackage = new PackageInfo
        {
            Name = "DeepPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            DependencyDepth = 25
        };

        var shallowPackage = new PackageInfo
        {
            Name = "ShallowPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            DependencyDepth = 11
        };

        riskEvaluator.EvaluateRisk(deepPackage);
        riskEvaluator.EvaluateRisk(shallowPackage);

        deepPackage.RiskDimensions.SecurityRisk.Should()
            .BeGreaterThan(shallowPackage.RiskDimensions.SecurityRisk);
        deepPackage.RiskDimensions.SecurityRiskRationale.Should()
            .Contain(item => item.Contains("Deep dependency chain (depth 25)"));
    }

    [TestMethod]
    internal void Should_add_security_risk_for_abandoned_transitive_dependencies()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            AbandonedTransitiveDependencyCount = 2
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.SecurityRiskRationale.Should()
            .Contain(item => item.Contains("Potentially abandoned risky transitive dependencies were detected (2)"));
    }

    [TestMethod]
    internal void Should_add_security_risk_when_owner_account_is_less_than_one_year_old()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            OwnerCreatedAt = DateTimeOffset.UtcNow.AddMonths(-6)
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.SecurityRiskRationale.Should()
            .Contain(item => item.Contains("Repository owner account is less than one year old"));
    }

    [TestMethod]
    internal void Should_add_security_risk_for_packages_with_native_binary_assets()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            HasNativeBinaryAssets = true,
            HasSigningRiskData = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.SecurityRiskRationale.Should()
            .Contain(item => item.Contains("Package contains native or binary assets that may increase supply-chain exposure"));
    }

    [TestMethod]
    internal void Should_add_security_risk_when_coordinated_disclosure_is_absent()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var packageWithoutDisclosure = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            HasCoordinatedDisclosure = false
        };

        var packageWithDisclosure = new PackageInfo
        {
            Name = "TestPackage2",
            Version = "1.0.0",
            License = "MIT",
            RepositoryUrl = "https://github.com/test/package",
            HasCoordinatedDisclosure = true
        };

        riskEvaluator.EvaluateRisk(packageWithoutDisclosure);
        riskEvaluator.EvaluateRisk(packageWithDisclosure);

        packageWithoutDisclosure.RiskDimensions.SecurityRisk.Should()
            .BeGreaterThan(packageWithDisclosure.RiskDimensions.SecurityRisk);
        packageWithoutDisclosure.RiskDimensions.SecurityRiskRationale.Should()
            .Contain(item => item.Contains("No coordinated disclosure signal was detected"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_high_prerelease_release_ratio()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            PrereleaseRatio = 0.6,
            ContributorCount = 5,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("High prerelease ratio detected"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_rapid_release_corrections()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            RapidReleaseCorrectionCount = 3,
            ContributorCount = 5,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("Rapid release corrections were detected (3)"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_release_older_than_12_but_not_24_months()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-15),
            ContributorCount = 5,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("Last release is older than 12 months"));
        package.RiskDimensions.OperationalRiskRationale.Should()
            .NotContain(item => item.Contains("Last release is older than 24 months"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_elevated_but_not_long_mean_release_interval()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            MeanReleaseIntervalDays = 250,
            ContributorCount = 5,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("Mean release interval is elevated"));
        package.RiskDimensions.OperationalRiskRationale.Should()
            .NotContain(item => item.Contains("Mean release interval is long"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_stale_readme_and_changelog()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            HasReadme = true,
            HasDefaultReadme = false,
            ReadmeUpdatedAt = DateTimeOffset.UtcNow.AddMonths(-24),
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasDetailedSecurityPolicy = false,
            HasChangelog = true,
            ChangelogUpdatedAt = DateTimeOffset.UtcNow.AddMonths(-24),
            ContributorCount = 5
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("README has not been refreshed recently"));
        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("SECURITY policy lacks concrete response instructions"));
        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("CHANGELOG has not been refreshed recently"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_very_few_active_maintainers()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            ContributorCount = 5,
            RecentMaintainerCount = 1,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("Very few active maintainers in the last 6 months (1)"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_limited_package_popularity()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            DownloadCount = 5000,
            ContributorCount = 5,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("Limited package popularity (5000 downloads)"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_version_update_lag_between_90_and_365_days()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            LatestStableVersion = "1.5.0",
            VersionUpdateLagDays = 120,
            IsMinorVersionBehindLatest = true,
            ContributorCount = 5,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("The current version trails the latest stable release (120.0 days)"));
        package.RiskDimensions.OperationalRiskRationale.Should()
            .NotContain(item => item.Contains("by a long time"));
    }

    [TestMethod]
    internal void Should_add_operational_risk_for_moderate_openssf_score()
    {
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "TestPackage",
            Version = "1.0.0",
            License = "MIT",
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            OpenSsfScore = 6.0,
            ContributorCount = 5,
            HasReadme = true,
            HasDefaultReadme = false,
            HasContributingGuide = true,
            HasSecurityPolicy = true,
            HasChangelog = true
        };

        riskEvaluator.EvaluateRisk(package);

        package.RiskDimensions.OperationalRiskRationale.Should()
            .Contain(item => item.Contains("OpenSSF Scorecard score is moderate (6.0)"));
    }
}
