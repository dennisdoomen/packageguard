using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console;

namespace PackageGuard.Specs;

[TestClass]
public class InitCommandSettingsSpecs
{
    [TestMethod]
    [DataRow("permissive-only")]
    [DataRow("no-network-copyleft")]
    [DataRow("oss-friendly")]
    public void Accepts_a_recognized_preset(string preset)
    {
        var settings = new InitCommandSettings { Preset = preset };

        settings.Validate().Successful.Should().BeTrue();
    }

    [TestMethod]
    public void Rejects_an_unrecognized_preset()
    {
        var settings = new InitCommandSettings { Preset = "bogus" };

        ValidationResult result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("--preset");
    }

    [TestMethod]
    public void Does_not_require_a_preset_when_running_interactively()
    {
        var settings = new InitCommandSettings();

        settings.Validate().Successful.Should().BeTrue();
    }

    [TestMethod]
    public void Never_enables_risk_reporting()
    {
        var settings = new InitCommandSettings();

        settings.ToCoreSettings().ReportRisk.Should().BeFalse();
    }

    [TestMethod]
    public void Accepts_risk_gates_with_or_without_a_preset()
    {
        new InitCommandSettings { RiskGates = true }.Validate().Successful.Should().BeTrue();
        new InitCommandSettings { RiskGates = true, Preset = "oss-friendly" }.Validate().Successful.Should().BeTrue();
    }
}
