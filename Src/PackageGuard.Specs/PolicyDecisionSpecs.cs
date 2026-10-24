#nullable enable
using System;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;
using PackageGuard.Core.Risk;

namespace PackageGuard.Specs;

[TestClass]
public class PolicyDecisionSpecs
{
    private static PackageInfo CreatePackage(string name = "Newtonsoft.Json", string version = "13.0.3", string? license = "MIT")
    {
        return new PackageInfo
        {
            Name = name,
            Version = version,
            License = license
        };
    }

    [TestMethod]
    public void Explains_a_package_allowed_by_a_matching_package_rule()
    {
        // Arrange
        var allowList = new AllowList
        {
            Packages = { new PackageSelector("Newtonsoft.Json") { SourceFile = "packageguard.config.json" } }
        };

        // Act
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage(name: "Newtonsoft.Json"));

        // Assert
        decision.IsMatch.Should().BeTrue();
        decision.Reason.Should().Contain("Newtonsoft.Json");
        decision.SourceFile.Should().Be("packageguard.config.json");
    }

    [TestMethod]
    public void Explains_a_package_allowed_by_a_matching_license_rule()
    {
        // Arrange
        var allowList = new AllowList
        {
            Licenses = { "MIT" },
            LicenseSourceFiles = { ["MIT"] = "packageguard.config.json" }
        };

        // Act
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage(license: "MIT"));

        // Assert
        decision.IsMatch.Should().BeTrue();
        decision.Reason.Should().Contain("MIT");
        decision.SourceFile.Should().Be("packageguard.config.json");
    }

    [TestMethod]
    public void Explains_a_package_rejected_because_its_license_is_not_allowed()
    {
        // Arrange
        var allowList = new AllowList { Licenses = { "Apache-2.0" } };

        // Act
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage(license: "MIT"));

        // Assert
        decision.IsMatch.Should().BeFalse();
        decision.Reason.Should().Contain("MIT");
    }

    [TestMethod]
    public void Explains_a_package_denied_by_a_matching_deny_package_rule()
    {
        // Arrange
        var denyList = new DenyList
        {
            Packages = { new PackageSelector("Newtonsoft.Json") { SourceFile = ".packageguard/config.json" } }
        };

        // Act
        PolicyDecision decision = denyList.EvaluateDeny(CreatePackage(name: "Newtonsoft.Json"));

        // Assert
        decision.IsMatch.Should().BeTrue();
        decision.Reason.Should().Contain("Newtonsoft.Json");
        decision.SourceFile.Should().Be(".packageguard/config.json");
    }

    [TestMethod]
    public void Explains_a_package_not_denied_by_any_rule()
    {
        // Arrange
        var denyList = new DenyList();

        // Act
        PolicyDecision decision = denyList.EvaluateDeny(CreatePackage());

        // Assert
        decision.IsMatch.Should().BeFalse();
    }

    [TestMethod]
    public void EvaluateAllow_and_Allows_agree_on_the_outcome()
    {
        // Arrange
        var allowList = new AllowList { Licenses = { "Apache-2.0" } };
        PackageInfo package = CreatePackage(license: "MIT");

        // Act & Assert
        allowList.Allows(package).Should().Be(allowList.EvaluateAllow(package).IsMatch);
    }

    [TestMethod]
    public void EvaluateDeny_and_Denies_agree_on_the_outcome()
    {
        // Arrange
        var denyList = new DenyList { Licenses = { "MIT" } };
        PackageInfo package = CreatePackage(license: "MIT");

        // Act & Assert
        denyList.Denies(package).Should().Be(denyList.EvaluateDeny(package).IsMatch);
    }

    [TestMethod]
    public void Denies_a_package_whose_overall_risk_score_exceeds_the_maximum()
    {
        // Arrange
        var denyList = new DenyList { MaxOverallRisk = 60 };
        PackageInfo package = CreatePackage();
        package.RiskScore = 75;

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.IsMatch.Should().BeTrue();
        decision.Reason.Should().Contain("75").And.Contain("60");
    }

    [TestMethod]
    public void Does_not_deny_a_package_whose_overall_risk_score_is_within_the_maximum()
    {
        // Arrange
        var denyList = new DenyList { MaxOverallRisk = 60 };
        PackageInfo package = CreatePackage();
        package.RiskScore = 60;

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.IsMatch.Should().BeFalse();
    }

    [TestMethod]
    public void Denies_a_package_whose_security_risk_dimension_exceeds_the_maximum()
    {
        // Arrange
        var denyList = new DenyList { MaxSecurityRisk = 7 };
        PackageInfo package = CreatePackage();
        package.RiskDimensions = new RiskDimensions { SecurityRisk = 8 };

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.IsMatch.Should().BeTrue();
        decision.Reason.Should().Contain("security");
    }

    [TestMethod]
    public void Denies_a_package_whose_highest_osv_severity_exceeds_the_maximum()
    {
        // Arrange
        var denyList = new DenyList { MaxOsvSeverityScore = 7.0 };
        PackageInfo package = CreatePackage();
        package.MaxVulnerabilitySeverity = 9.8;

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.IsMatch.Should().BeTrue();
        decision.Reason.Should().Contain("OSV");
    }

    [TestMethod]
    public void Denies_a_package_with_a_known_exploited_vulnerability_when_configured()
    {
        // Arrange
        var denyList = new DenyList { DenyKnownExploited = true };
        PackageInfo package = CreatePackage();
        package.Vulnerabilities = [new OsvVulnerabilityRecord { Id = "GHSA-x", Aliases = ["CVE-2024-0001"], IsKnownExploited = true }];

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        denyList.HasRiskPolicies.Should().BeTrue();
        decision.IsMatch.Should().BeTrue();
        decision.Reason.Should().Contain("CVE-2024-0001");
    }

    [TestMethod]
    public void Allows_a_package_without_a_known_exploited_vulnerability_even_when_configured()
    {
        // Arrange
        var denyList = new DenyList { DenyKnownExploited = true };
        PackageInfo package = CreatePackage();
        package.Vulnerabilities = [new OsvVulnerabilityRecord { Id = "GHSA-x", Aliases = ["CVE-2024-0001"] }];

        // Act & Assert
        denyList.EvaluateRiskDeny(package).IsMatch.Should().BeFalse();
    }

    [TestMethod]
    public void Denies_an_unsigned_package_when_configured()
    {
        // Arrange
        var denyList = new DenyList { DenyUnsigned = true };
        PackageInfo package = CreatePackage();
        package.IsPackageSigned = false;

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.IsMatch.Should().BeTrue();
    }

    [TestMethod]
    public void Does_not_deny_a_package_with_unknown_signing_status_when_denyUnsigned_is_set()
    {
        // Arrange
        var denyList = new DenyList { DenyUnsigned = true };
        PackageInfo package = CreatePackage();
        package.IsPackageSigned = null;

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.IsMatch.Should().BeFalse();
    }

    [TestMethod]
    public void Denies_a_deprecated_package_when_configured()
    {
        // Arrange
        var denyList = new DenyList { DenyDeprecated = true };
        PackageInfo package = CreatePackage();
        package.IsDeprecated = true;

        // Act & Assert
        denyList.EvaluateRiskDeny(package).IsMatch.Should().BeTrue();
    }

    [TestMethod]
    public void Denies_a_package_without_a_repository_url_when_configured()
    {
        // Arrange
        var denyList = new DenyList { DenyWithoutRepository = true };
        PackageInfo package = CreatePackage();
        package.RepositoryUrl = null;

        // Act & Assert
        denyList.EvaluateRiskDeny(package).IsMatch.Should().BeTrue();
    }

    [TestMethod]
    public void Denies_a_package_published_more_recently_than_the_minimum_age()
    {
        // Arrange
        var denyList = new DenyList { MinPackageAgeDays = { ["nuget"] = 14 } };
        PackageInfo package = CreatePackage();
        package.PublishedAt = DateTimeOffset.UtcNow.AddDays(-2);

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.IsMatch.Should().BeTrue();
        decision.Reason.Should().Contain("14");
    }

    [TestMethod]
    public void Does_not_deny_a_package_older_than_the_minimum_age()
    {
        // Arrange
        var denyList = new DenyList { MinPackageAgeDays = { ["nuget"] = 14 } };
        PackageInfo package = CreatePackage();
        package.PublishedAt = DateTimeOffset.UtcNow.AddDays(-30);

        // Act & Assert
        denyList.EvaluateRiskDeny(package).IsMatch.Should().BeFalse();
    }

    [TestMethod]
    public void HasRiskPolicies_is_false_for_a_plain_deny_list()
    {
        new DenyList().HasRiskPolicies.Should().BeFalse();
    }

    [TestMethod]
    public void HasRiskPolicies_is_true_once_a_risk_rule_is_configured()
    {
        new DenyList { MaxOverallRisk = 60 }.HasRiskPolicies.Should().BeTrue();
    }

    [TestMethod]
    public void A_risk_exception_excludes_a_matching_package_from_risk_based_denial()
    {
        // Arrange
        var exception = new RiskException { Package = "Newtonsoft.Json", Reason = "vetted" };
        PackageInfo package = CreatePackage(name: "Newtonsoft.Json");

        // Act & Assert
        exception.Matches(package).Should().BeTrue();
    }

    [TestMethod]
    public void A_risk_exception_does_not_match_a_different_package()
    {
        // Arrange
        var exception = new RiskException { Package = "SomeOtherPackage" };
        PackageInfo package = CreatePackage(name: "Newtonsoft.Json");

        // Act & Assert
        exception.Matches(package).Should().BeFalse();
    }

    [TestMethod]
    public void A_risk_exception_stops_applying_after_it_expires()
    {
        // Arrange
        var exception = new RiskException
        {
            Package = "Newtonsoft.Json",
            ExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
        };
        PackageInfo package = CreatePackage(name: "Newtonsoft.Json");

        // Act & Assert
        exception.Matches(package).Should().BeFalse();
    }

    [TestMethod]
    public void A_risk_exception_can_be_pinned_to_a_version_range()
    {
        // Arrange
        var exception = new RiskException { Package = "Newtonsoft.Json", Versions = "[1.0.0,2.0.0)" };
        PackageInfo package = CreatePackage(name: "Newtonsoft.Json", version: "13.0.3");

        // Act & Assert
        exception.Matches(package).Should().BeFalse();
    }

    [TestMethod]
    public void Explains_a_package_flagged_by_a_matching_warn_license_rule()
    {
        // Arrange
        var warnList = new WarnList
        {
            Licenses = { "MIT" },
            LicenseSourceFiles = { ["MIT"] = "packageguard.config.json" }
        };

        // Act
        PolicyDecision decision = warnList.EvaluateWarn(CreatePackage(license: "MIT"));

        // Assert
        decision.IsMatch.Should().BeTrue();
        decision.Reason.Should().Contain("MIT");
    }

    [TestMethod]
    public void Explains_a_package_not_flagged_by_any_warn_rule()
    {
        new WarnList().EvaluateWarn(CreatePackage()).IsMatch.Should().BeFalse();
    }

    [TestMethod]
    public void EvaluateWarn_and_Warns_agree_on_the_outcome()
    {
        // Arrange
        var warnList = new WarnList { Licenses = { "GPL-3.0" } };
        PackageInfo package = CreatePackage(license: "MIT");

        // Act & Assert
        warnList.Warns(package).Should().Be(warnList.EvaluateWarn(package).IsMatch);
    }

    [TestMethod]
    public void Suggests_changing_the_allow_list_for_a_license_violation_without_mentioning_risk_exceptions()
    {
        // Arrange
        var allowList = new AllowList { Licenses = { "Apache-2.0" } };

        // Act
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage(license: "MIT"));

        // Assert
        decision.Resolution.Should().Contain("allow section").And.Contain("riskExceptions do not exempt");
    }

    [TestMethod]
    public void Suggests_a_version_in_range_for_a_package_outside_its_allowed_version_range()
    {
        // Arrange
        var allowList = new AllowList { Packages = { new PackageSelector("Newtonsoft.Json", "[14.0.0,)") } };

        // Act
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage(name: "Newtonsoft.Json", version: "13.0.3"));

        // Assert
        decision.Resolution.Should().Contain("[14.0.0,)");
    }

    [TestMethod]
    public void Suggests_a_stable_version_for_a_prerelease_package_the_allow_list_does_not_allow()
    {
        // Arrange
        var allowList = new AllowList { Prerelease = false };

        // Act
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage(version: "14.0.0-beta.1"));

        // Assert
        decision.Resolution.Should().Contain("stable version").And.Contain("allow section");
    }

    [TestMethod]
    public void Suggests_removing_the_deny_entry_or_using_another_package_for_a_denied_package()
    {
        // Arrange
        var denyList = new DenyList { Packages = { new PackageSelector("Newtonsoft.Json") } };

        // Act
        PolicyDecision decision = denyList.EvaluateDeny(CreatePackage(name: "Newtonsoft.Json"));

        // Assert
        decision.Resolution.Should().Contain("Newtonsoft.Json").And.Contain("deny section").And.NotContain("riskExceptions");
    }

    [TestMethod]
    public void Names_the_configuration_file_that_introduced_the_deny_entry()
    {
        // Arrange
        var denyList = new DenyList
        {
            Packages = { new PackageSelector("Newtonsoft.Json") { SourceFile = "src/.packageguard/config.json" } }
        };

        // Act
        PolicyDecision decision = denyList.EvaluateDeny(CreatePackage(name: "Newtonsoft.Json"));

        // Assert
        decision.Resolution.Should().Contain("deny section of src/.packageguard/config.json");
    }

    [TestMethod]
    public void Suggests_removing_the_deny_entry_or_using_another_package_for_a_denied_license()
    {
        // Arrange
        var denyList = new DenyList { Licenses = { "MIT" } };

        // Act
        PolicyDecision decision = denyList.EvaluateDeny(CreatePackage(license: "MIT"));

        // Assert
        decision.Resolution.Should().Contain("different license").And.NotContain("riskExceptions");
    }

    [TestMethod]
    public void Suggests_a_stable_version_for_a_prerelease_package_the_deny_list_denies()
    {
        // Arrange
        var denyList = new DenyList { Prerelease = true };

        // Act
        PolicyDecision decision = denyList.EvaluateDeny(CreatePackage(version: "14.0.0-beta.1"));

        // Assert
        decision.Resolution.Should().Contain("stable version").And.Contain("deny section");
    }

    [TestMethod]
    public void Suggests_a_risk_exception_for_a_package_denied_by_a_risk_gate()
    {
        // Arrange
        var denyList = new DenyList { DenyUnsigned = true };
        PackageInfo package = CreatePackage();
        package.IsPackageSigned = false;

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.Resolution.Should().Contain("riskExceptions").And.Contain("expiresOn");
    }

    [TestMethod]
    public void Suggests_an_older_version_instead_of_an_upgrade_for_a_package_that_is_too_new()
    {
        // Arrange
        var denyList = new DenyList { MinPackageAgeDays = { ["nuget"] = 14 } };
        PackageInfo package = CreatePackage();
        package.PublishedAt = DateTimeOffset.UtcNow.AddDays(-2);

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.Resolution.Should().Contain("older version").And.Contain("riskExceptions").And.NotContain("Upgrade");
    }

    [TestMethod]
    public void Suggests_a_vulnerability_exception_for_a_package_denied_by_a_vulnerability_gate()
    {
        // Arrange
        var denyList = new DenyList { MaxOsvSeverityScore = 7.0 };
        PackageInfo package = CreatePackage();
        package.MaxVulnerabilitySeverity = 9.8;

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.Resolution.Should().Contain("vulnerabilityExceptions");
    }

    [TestMethod]
    public void Suggests_a_vulnerability_exception_for_a_package_with_a_known_exploited_vulnerability()
    {
        // Arrange
        var denyList = new DenyList { DenyKnownExploited = true };
        PackageInfo package = CreatePackage();
        package.Vulnerabilities = [new OsvVulnerabilityRecord { Id = "GHSA-x", Aliases = ["CVE-2024-0001"], IsKnownExploited = true }];

        // Act
        PolicyDecision decision = denyList.EvaluateRiskDeny(package);

        // Assert
        decision.Resolution.Should().Contain("vulnerabilityExceptions");
    }
}
