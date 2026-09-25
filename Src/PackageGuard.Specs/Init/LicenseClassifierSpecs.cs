using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Init;

namespace PackageGuard.Specs.Init;

[TestClass]
public class LicenseClassifierSpecs
{
    [TestMethod]
    [DataRow("MIT")]
    [DataRow("Apache-2.0")]
    [DataRow("BSD-3-Clause")]
    [DataRow("ISC")]
    [DataRow("MS-PL")]
    public void Classifies_common_permissive_licenses_as_permissive(string license)
    {
        LicenseClassifier.Classify(license).Should().Be(LicenseCategory.Permissive);
    }

    [TestMethod]
    [DataRow("LGPL-2.1-only")]
    [DataRow("LGPL-3.0-or-later")]
    [DataRow("MPL-2.0")]
    public void Classifies_weak_copyleft_licenses(string license)
    {
        LicenseClassifier.Classify(license).Should().Be(LicenseCategory.WeakCopyleft);
    }

    [TestMethod]
    [DataRow("GPL-2.0-only")]
    [DataRow("GPL-3.0-or-later")]
    public void Classifies_strong_copyleft_licenses(string license)
    {
        LicenseClassifier.Classify(license).Should().Be(LicenseCategory.StrongCopyleft);
    }

    [TestMethod]
    [DataRow("AGPL-3.0-only")]
    [DataRow("AGPL-3.0-or-later")]
    public void Classifies_network_copyleft_licenses(string license)
    {
        LicenseClassifier.Classify(license).Should().Be(LicenseCategory.NetworkCopyleft);
    }

    [TestMethod]
    public void Classification_is_case_insensitive()
    {
        LicenseClassifier.Classify("mit").Should().Be(LicenseCategory.Permissive);
        LicenseClassifier.Classify("gpl-3.0-only").Should().Be(LicenseCategory.StrongCopyleft);
    }

    [TestMethod]
    public void Classifies_a_missing_license_as_unknown()
    {
        LicenseClassifier.Classify(null).Should().Be(LicenseCategory.Unknown);
        LicenseClassifier.Classify("").Should().Be(LicenseCategory.Unknown);
        LicenseClassifier.Classify("   ").Should().Be(LicenseCategory.Unknown);
    }

    [TestMethod]
    public void Classifies_an_unrecognized_license_as_unknown_rather_than_assuming_it_is_safe()
    {
        LicenseClassifier.Classify("SomeObscureLicense-1.0").Should().Be(LicenseCategory.Unknown);
    }

    [TestMethod]
    public void Only_copyleft_categories_are_reported_as_copyleft()
    {
        LicenseClassifier.IsCopyleft(LicenseCategory.Permissive).Should().BeFalse();
        LicenseClassifier.IsCopyleft(LicenseCategory.Unknown).Should().BeFalse();
        LicenseClassifier.IsCopyleft(LicenseCategory.WeakCopyleft).Should().BeTrue();
        LicenseClassifier.IsCopyleft(LicenseCategory.StrongCopyleft).Should().BeTrue();
        LicenseClassifier.IsCopyleft(LicenseCategory.NetworkCopyleft).Should().BeTrue();
    }
}
