using System;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Scaffolding;

namespace PackageGuard.Specs.Scaffolding;

[TestClass]
public class LicensePresetsSpecs
{
    [TestMethod]
    public void Proprietary_software_only_tolerates_permissive_licenses()
    {
        var allowed = LicensePresets.GetAllowedCategories(SoftwareProfile.Proprietary);

        allowed.Should().BeEquivalentTo([LicenseCategory.Permissive]);
    }

    [TestMethod]
    public void Saas_software_tolerates_permissive_and_weak_copyleft_but_not_strong_or_network_copyleft()
    {
        var allowed = LicensePresets.GetAllowedCategories(SoftwareProfile.Saas);

        allowed.Should().BeEquivalentTo([LicenseCategory.Permissive, LicenseCategory.WeakCopyleft]);
    }

    [TestMethod]
    public void Open_source_software_tolerates_every_copyleft_category()
    {
        var allowed = LicensePresets.GetAllowedCategories(SoftwareProfile.OpenSource);

        allowed.Should().BeEquivalentTo(
        [
            LicenseCategory.Permissive, LicenseCategory.WeakCopyleft, LicenseCategory.StrongCopyleft, LicenseCategory.NetworkCopyleft
        ]);
    }

    [TestMethod]
    [DataRow("Proprietary", "permissive-only")]
    [DataRow("Saas", "no-network-copyleft")]
    [DataRow("OpenSource", "oss-friendly")]
    public void Maps_each_profile_to_its_stable_preset_name(string profileName, string expectedName)
    {
        var profile = Enum.Parse<SoftwareProfile>(profileName);

        LicensePresets.GetPresetName(profile).Should().Be(expectedName);
    }

    [TestMethod]
    [DataRow("permissive-only", "Proprietary")]
    [DataRow("no-network-copyleft", "Saas")]
    [DataRow("oss-friendly", "OpenSource")]
    [DataRow("OSS-FRIENDLY", "OpenSource")]
    [DataRow("  oss-friendly  ", "OpenSource")]
    public void Parses_a_recognized_preset_name(string presetName, string expectedProfileName)
    {
        bool parsed = LicensePresets.TryParsePresetName(presetName, out SoftwareProfile profile);

        parsed.Should().BeTrue();
        profile.Should().Be(Enum.Parse<SoftwareProfile>(expectedProfileName));
    }

    [TestMethod]
    public void Rejects_an_unrecognized_preset_name()
    {
        bool parsed = LicensePresets.TryParsePresetName("bogus", out _);

        parsed.Should().BeFalse();
    }

    [TestMethod]
    public void Every_preset_name_round_trips_through_its_profile()
    {
        foreach (string presetName in LicensePresets.PresetNames)
        {
            LicensePresets.TryParsePresetName(presetName, out SoftwareProfile profile).Should().BeTrue();
            LicensePresets.GetPresetName(profile).Should().Be(presetName);
        }
    }
}
