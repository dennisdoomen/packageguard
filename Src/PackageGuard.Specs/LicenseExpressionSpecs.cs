#nullable enable
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;

namespace PackageGuard.Specs;

[TestClass]
public class LicenseExpressionSpecs
{
    private static PackageInfo CreatePackage(string? license) => new()
    {
        Name = "SomePackage",
        Version = "1.0.0",
        License = license
    };

    private static bool IsAllowed(string license, params string[] allowed) =>
        new AllowList { Licenses = [..allowed] }.EvaluateAllow(CreatePackage(license)).IsMatch;

    private static bool IsDenied(string license, params string[] denied) =>
        new DenyList { Licenses = [..denied] }.EvaluateDeny(CreatePackage(license)).IsMatch;

    [TestMethod]
    [DataRow("MIT OR Apache-2.0")]
    [DataRow("Apache-2.0 OR MIT")]
    [DataRow("GPL-3.0-only OR MIT")]
    [DataRow("(GPL-3.0-only OR MIT)")]
    [DataRow("MIT or GPL-3.0-only")]
    public void Allows_an_or_expression_if_any_option_is_allowed(string license)
    {
        IsAllowed(license, "MIT").Should().BeTrue();
    }

    [TestMethod]
    public void Allows_an_and_expression_only_if_every_license_is_allowed()
    {
        IsAllowed("MIT AND Unicode-DFS-2016", "MIT").Should().BeFalse();
        IsAllowed("MIT AND Unicode-DFS-2016", "MIT", "Unicode-DFS-2016").Should().BeTrue();
    }

    [TestMethod]
    public void Respects_parentheses_in_nested_expressions()
    {
        IsAllowed("(MIT OR Apache-2.0) AND Unicode-DFS-2016", "Apache-2.0", "Unicode-DFS-2016").Should().BeTrue();
        IsAllowed("(MIT OR Apache-2.0) AND Unicode-DFS-2016", "Apache-2.0").Should().BeFalse();
        IsAllowed("MIT OR (Apache-2.0 AND Unicode-DFS-2016)", "Apache-2.0").Should().BeFalse();
    }

    [TestMethod]
    public void Allows_a_license_with_an_exception_by_its_full_term_or_its_base_license()
    {
        IsAllowed("GPL-2.0-only WITH Classpath-exception-2.0", "GPL-2.0-only WITH Classpath-exception-2.0").Should().BeTrue();
        IsAllowed("GPL-2.0-only WITH Classpath-exception-2.0", "GPL-2.0-only").Should().BeTrue();
        IsAllowed("GPL-2.0-only", "GPL-2.0-only WITH Classpath-exception-2.0").Should().BeFalse();
    }

    [TestMethod]
    public void Allows_an_entry_that_is_an_expression_itself()
    {
        IsAllowed("(MIT OR Apache-2.0)", "MIT OR Apache-2.0").Should().BeTrue();
    }

    [TestMethod]
    [DataRow("Apache-2.0 OR MIT", "MIT OR Apache-2.0")]
    [DataRow("BSD-3-Clause OR (Apache-2.0 OR MIT)", "MIT OR Apache-2.0 OR BSD-3-Clause")]
    [DataRow("(MIT AND BSD-3-Clause) OR Apache-2.0", "Apache-2.0 OR (BSD-3-Clause AND MIT)")]
    public void Ignores_the_order_of_the_operands_when_matching_an_expression_entry(string license, string entry)
    {
        IsAllowed(license, entry).Should().BeTrue();
    }

    [TestMethod]
    public void Denies_an_or_expression_only_if_every_option_is_denied()
    {
        IsDenied("GPL-3.0-only OR MIT", "GPL-3.0-only").Should().BeFalse();
        IsDenied("GPL-3.0-only OR AGPL-3.0-only", "GPL-3.0-only", "AGPL-3.0-only").Should().BeTrue();
    }

    [TestMethod]
    public void Denies_an_and_expression_if_any_license_is_denied()
    {
        IsDenied("MIT AND GPL-3.0-only", "GPL-3.0-only").Should().BeTrue();
    }

    [TestMethod]
    public void Does_not_deny_a_license_with_an_exception_by_its_base_license()
    {
        IsDenied("GPL-2.0-only WITH Classpath-exception-2.0", "GPL-2.0-only").Should().BeFalse();
        IsDenied("GPL-2.0-only WITH Classpath-exception-2.0", "GPL-2.0-only WITH Classpath-exception-2.0").Should().BeTrue();
    }

    [TestMethod]
    [DataRow("GPL-3.0", "GPL-3.0-only")]
    [DataRow("GPL-3.0-only", "GPL-3.0")]
    [DataRow("GPL-2.0+", "GPL-2.0-or-later")]
    [DataRow("LGPL-2.1", "LGPL-2.1-only")]
    public void Treats_deprecated_identifiers_as_their_current_form(string license, string entry)
    {
        IsDenied(license, entry).Should().BeTrue();
    }

    [TestMethod]
    public void Allows_an_or_later_license_if_its_base_version_is_allowed()
    {
        IsAllowed("Apache-2.0+", "Apache-2.0").Should().BeTrue();
        IsAllowed("GPL-2.0-or-later", "GPL-2.0-only").Should().BeTrue();
        IsDenied("Apache-2.0+", "Apache-2.0").Should().BeFalse();
    }

    [TestMethod]
    public void Matches_free_text_licenses_as_they_are()
    {
        IsAllowed("Microsoft .NET Library License", "Microsoft .NET Library License").Should().BeTrue();
        IsAllowed("Unknown", "MIT").Should().BeFalse();
        IsAllowed("MIT OR", "MIT").Should().BeFalse("an incomplete expression is not a valid license");
    }

    [TestMethod]
    public void Names_the_option_that_satisfied_the_policy()
    {
        // Arrange
        var allowList = new AllowList
        {
            Licenses = { "Apache-2.0" },
            LicenseSourceFiles = { ["Apache-2.0"] = "packageguard.config.json" }
        };

        // Act
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage("MIT OR Apache-2.0"));

        // Assert
        decision.Reason.Should().Be("matches allow list license entry \"Apache-2.0\" (from \"MIT OR Apache-2.0\")");
        decision.SourceFile.Should().Be("packageguard.config.json");
    }

    [TestMethod]
    public void Names_every_entry_that_allowed_an_and_expression()
    {
        // Arrange
        var allowList = new AllowList
        {
            Licenses = { "MIT", "Unicode-DFS-2016" },
            LicenseSourceFiles =
            {
                ["MIT"] = "packageguard.config.json",
                ["Unicode-DFS-2016"] = "src/packageguard.config.json"
            }
        };

        // Act
        PolicyDecision decision = allowList.EvaluateAllow(CreatePackage("MIT AND Unicode-DFS-2016"));

        // Assert
        decision.Reason.Should().Be(
            "matches allow list license entries \"MIT\" and \"Unicode-DFS-2016\" (from \"MIT AND Unicode-DFS-2016\")");
        decision.SourceFile.Should().Be("packageguard.config.json, src/packageguard.config.json");
    }

    [TestMethod]
    public void Names_every_entry_that_denied_an_or_expression()
    {
        // Arrange
        var denyList = new DenyList { Licenses = { "GPL-3.0-only", "AGPL-3.0-only" } };

        // Act
        PolicyDecision decision = denyList.EvaluateDeny(CreatePackage("GPL-3.0-only OR AGPL-3.0-only"));

        // Assert
        decision.Reason.Should().Be(
            "matches deny list license entries \"GPL-3.0-only\" and \"AGPL-3.0-only\" (from \"GPL-3.0-only OR AGPL-3.0-only\")");
        decision.Resolution.Should().Contain("remove \"GPL-3.0-only\" or \"AGPL-3.0-only\"");
    }

    [TestMethod]
    public void Warns_about_an_and_expression_that_includes_a_warn_license()
    {
        new WarnList { Licenses = { "LGPL-3.0-only" } }.EvaluateWarn(CreatePackage("MIT AND LGPL-3.0")).IsMatch
            .Should().BeTrue();
    }
}
