#nullable enable
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Init;
using PackageGuard.Core.Package;

namespace PackageGuard.Specs.Init;

[TestClass]
public class LicenseUsageSummarizerSpecs
{
    private static PackageInfo CreatePackage(string name, string version, string? license)
    {
        return new PackageInfo { Name = name, Version = version, License = license };
    }

    [TestMethod]
    public void Groups_packages_by_license_and_counts_them()
    {
        PackageInfo[] packages =
        [
            CreatePackage("A", "1.0.0", "MIT"),
            CreatePackage("B", "1.0.0", "MIT"),
            CreatePackage("C", "1.0.0", "Apache-2.0")
        ];

        var usages = LicenseUsageSummarizer.Summarize(packages);

        usages.Should().HaveCount(2);
        usages.Single(u => u.License == "MIT").PackageCount.Should().Be(2);
        usages.Single(u => u.License == "Apache-2.0").PackageCount.Should().Be(1);
    }

    [TestMethod]
    public void Orders_results_by_package_count_descending()
    {
        PackageInfo[] packages =
        [
            CreatePackage("A", "1.0.0", "Apache-2.0"),
            CreatePackage("B", "1.0.0", "MIT"),
            CreatePackage("C", "1.0.0", "MIT")
        ];

        var usages = LicenseUsageSummarizer.Summarize(packages);

        usages.Select(u => u.License).Should().Equal("MIT", "Apache-2.0");
    }

    [TestMethod]
    public void Groups_packages_with_no_resolved_license_under_a_null_license()
    {
        PackageInfo[] packages = [CreatePackage("A", "1.0.0", null)];

        var usages = LicenseUsageSummarizer.Summarize(packages);

        usages.Should().ContainSingle();
        usages[0].License.Should().BeNull();
        usages[0].Category.Should().Be(LicenseCategory.Unknown);
    }

    [TestMethod]
    public void Classifies_each_group_using_the_license_classifier()
    {
        PackageInfo[] packages = [CreatePackage("A", "1.0.0", "GPL-3.0-only")];

        var usages = LicenseUsageSummarizer.Summarize(packages);

        usages.Single().Category.Should().Be(LicenseCategory.StrongCopyleft);
    }

    [TestMethod]
    public void Collects_a_handful_of_example_packages_per_license()
    {
        PackageInfo[] packages =
        [
            CreatePackage("A", "1.0.0", "MIT"),
            CreatePackage("B", "1.0.0", "MIT"),
            CreatePackage("C", "1.0.0", "MIT"),
            CreatePackage("D", "1.0.0", "MIT")
        ];

        var usages = LicenseUsageSummarizer.Summarize(packages);

        usages.Single().ExamplePackages.Should().HaveCount(3);
    }

    [TestMethod]
    public void License_grouping_is_case_insensitive()
    {
        PackageInfo[] packages = [CreatePackage("A", "1.0.0", "MIT"), CreatePackage("B", "1.0.0", "mit")];

        var usages = LicenseUsageSummarizer.Summarize(packages);

        usages.Should().ContainSingle();
        usages[0].PackageCount.Should().Be(2);
    }
}
