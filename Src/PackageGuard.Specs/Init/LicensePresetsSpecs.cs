using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Init;

namespace PackageGuard.Specs.Init;

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
    [DataRow(SoftwareProfile.Proprietary, "permissive-only")]
    [DataRow(SoftwareProfile.Saas, "no-network-copyleft")]
    [DataRow(SoftwareProfile.OpenSource, "oss-friendly")]
    public void Maps_each_profile_to_its_stable_preset_name(SoftwareProfile profile, string expectedName)
    {
        LicensePresets.GetPresetName(profile).Should().Be(expectedName);
    }

    [TestMethod]
    [DataRow("permissive-only", SoftwareProfile.Proprietary)]
    [DataRow("no-network-copyleft", SoftwareProfile.Saas)]
    [DataRow("oss-friendly", SoftwareProfile.OpenSource)]
    [DataRow("OSS-FRIENDLY", SoftwareProfile.OpenSource)]
    [DataRow("  oss-friendly  ", SoftwareProfile.OpenSource)]
    public void Parses_a_recognized_preset_name(string presetName, SoftwareProfile expectedProfile)
    {
        bool parsed = LicensePresets.TryParsePresetName(presetName, out SoftwareProfile profile);

        parsed.Should().BeTrue();
        profile.Should().Be(expectedProfile);
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
