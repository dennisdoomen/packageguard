#nullable enable
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;

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
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage());

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
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage());

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
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage());

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
        PolicyDecision decision = denyList.EvaluateDeny(CreatePackage());

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
        PackageInfo package = CreatePackage();

        // Act & Assert
        allowList.Allows(package).Should().Be(allowList.EvaluateAllow(package).IsMatch);
    }

    [TestMethod]
    public void EvaluateDeny_and_Denies_agree_on_the_outcome()
    {
        // Arrange
        var denyList = new DenyList { Licenses = { "MIT" } };
        PackageInfo package = CreatePackage();

        // Act & Assert
        denyList.Denies(package).Should().Be(denyList.EvaluateDeny(package).IsMatch);
    }
}
