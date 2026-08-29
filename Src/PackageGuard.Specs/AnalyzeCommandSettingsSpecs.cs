using System.Threading;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PackageGuard.Specs;

[TestClass]
public class AnalyzeCommandSettingsSpecs
{
    [TestMethod]
    public void Bare_report_risk_flag_enables_risk_reporting_without_an_explicit_path()
    {
        var settings = new AnalyzeCommandSettings
        {
            ReportRiskOption = new FlagValue<string> { IsSet = true }
        };

        settings.ReportRisk.Should().BeTrue();
        settings.GetReportRiskPath().Should().BeNull();
        settings.ToCoreSettings().ReportRisk.Should().BeTrue();
    }

    [TestMethod]
    public void Report_risk_flag_exposes_an_explicitly_provided_path()
    {
        var settings = new AnalyzeCommandSettings
        {
            ReportRiskOption = new FlagValue<string> { IsSet = true, Value = @"C:\temp\risk.html" }
        };

        settings.ReportRisk.Should().BeTrue();
        settings.GetReportRiskPath().Should().Be(@"C:\temp\risk.html");
    }

    [TestMethod]
    public void Report_risk_path_is_null_when_the_flag_was_never_set()
    {
        var settings = new AnalyzeCommandSettings();

        settings.ReportRisk.Should().BeFalse();
        settings.GetReportRiskPath().Should().BeNull();
    }

    [TestMethod]
    [DataRow(new[] { "--report-risk" }, null)]
    [DataRow(new[] { "--report-risk", @"C:\temp\risk.html" }, @"C:\temp\risk.html")]
    [DataRow(new[] { "--report-risk=C:\\temp\\risk.html" }, @"C:\temp\risk.html")]
    [DataRow(new[] { "--reportrisk", @"C:\temp\risk.html" }, @"C:\temp\risk.html")]
    public void Spectre_binds_the_optional_report_risk_path_from_the_command_line(string[] args, string expectedPath)
    {
        var app = new CommandApp<CapturingCommand>();
        app.Configure(configurator => configurator.PropagateExceptions());

        CapturingCommand.LastSettings = null;
        app.Run(args);

        CapturingCommand.LastSettings.Should().NotBeNull();
        CapturingCommand.LastSettings.ReportRisk.Should().BeTrue();
        CapturingCommand.LastSettings.GetReportRiskPath().Should().Be(expectedPath);
    }

    /// <summary>
    /// A no-op command used to capture the <see cref="AnalyzeCommandSettings"/> that Spectre.Console.Cli bound
    /// from the command line, so the actual parsing behavior of <see cref="AnalyzeCommandSettings.ReportRiskOption"/>
    /// can be verified end-to-end.
    /// </summary>
    private sealed class CapturingCommand : Command<AnalyzeCommandSettings>
    {
        public static AnalyzeCommandSettings LastSettings { get; set; }

        protected override int Execute(CommandContext context, AnalyzeCommandSettings settings, CancellationToken cancellationToken)
        {
            LastSettings = settings;
            return 0;
        }
    }

    [TestMethod]
    [DataRow("cyclonedx")]
    [DataRow("CycloneDX")]
    [DataRow("spdx")]
    [DataRow("SPDX")]
    public void Accepts_a_recognized_sbom_format_when_an_output_path_is_given(string format)
    {
        var settings = new AnalyzeCommandSettings { Sbom = format, SbomOutput = "bom.json" };

        settings.Validate().Successful.Should().BeTrue();
    }

    [TestMethod]
    public void Rejects_an_unrecognized_sbom_format()
    {
        var settings = new AnalyzeCommandSettings { Sbom = "bogus", SbomOutput = "bom.json" };

        ValidationResult result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("--sbom");
    }

    [TestMethod]
    public void Requires_an_output_path_when_sbom_is_specified()
    {
        var settings = new AnalyzeCommandSettings { Sbom = "cyclonedx" };

        ValidationResult result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("--sbom-output");
    }

    [TestMethod]
    public void Does_not_require_an_sbom_format_or_output_path_when_sbom_generation_is_not_requested()
    {
        var settings = new AnalyzeCommandSettings();

        settings.Validate().Successful.Should().BeTrue();
    }
}
