#nullable enable
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Init;
using PackageGuard.Core.Package;

namespace PackageGuard.Specs.Init;

[TestClass]
public class InitPolicyBuilderSpecs
{
    private static PackageInfo CreatePackage(string name, string? license, string version = "1.0.0")
    {
        return new PackageInfo { Name = name, Version = version, License = license };
    }

    [TestMethod]
    public void Includes_only_licenses_that_were_actually_found()
    {
        LicenseUsage[] usages = [new("MIT", 5, LicenseCategory.Permissive, [])];

        var allowed = InitPolicyBuilder.BuildAllowedLicenses(usages, SoftwareProfile.OpenSource);

        allowed.Should().Equal("MIT");
    }

    [TestMethod]
    public void Excludes_licenses_outside_the_profiles_tolerated_categories()
    {
        LicenseUsage[] usages =
        [
            new("MIT", 5, LicenseCategory.Permissive, []),
            new("GPL-3.0-only", 1, LicenseCategory.StrongCopyleft, [])
        ];

        var allowed = InitPolicyBuilder.BuildAllowedLicenses(usages, SoftwareProfile.Proprietary);

        allowed.Should().Equal("MIT");
    }

    [TestMethod]
    public void Never_allows_a_null_license_regardless_of_profile()
    {
        LicenseUsage[] usages = [new(null, 1, LicenseCategory.Unknown, ["Obscure 1.0.0"])];

        var allowed = InitPolicyBuilder.BuildAllowedLicenses(usages, SoftwareProfile.OpenSource);

        allowed.Should().BeEmpty();
    }

    [TestMethod]
    public void Orders_the_allowed_licenses_alphabetically()
    {
        LicenseUsage[] usages =
        [
            new("MIT", 5, LicenseCategory.Permissive, []),
            new("Apache-2.0", 3, LicenseCategory.Permissive, [])
        ];

        var allowed = InitPolicyBuilder.BuildAllowedLicenses(usages, SoftwareProfile.Proprietary);

        allowed.Should().Equal("Apache-2.0", "MIT");
    }

    [TestMethod]
    public void Counts_packages_whose_license_is_not_in_the_allowed_list_as_violations()
    {
        PackageInfo[] packages = [CreatePackage("A", "MIT"), CreatePackage("B", "GPL-3.0-only")];

        int violations = InitPolicyBuilder.CountViolations(packages, ["MIT"]);

        violations.Should().Be(1);
    }

    [TestMethod]
    public void Counts_a_package_with_no_resolved_license_as_a_violation()
    {
        PackageInfo[] packages = [CreatePackage("A", null)];

        int violations = InitPolicyBuilder.CountViolations(packages, ["MIT"]);

        violations.Should().Be(1);
    }

    [TestMethod]
    public void Reports_zero_violations_when_every_package_license_is_allowed()
    {
        PackageInfo[] packages = [CreatePackage("A", "MIT"), CreatePackage("B", "MIT")];

        int violations = InitPolicyBuilder.CountViolations(packages, ["MIT"]);

        violations.Should().Be(0);
    }
}
