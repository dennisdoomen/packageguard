using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace PackageGuard.Specs;

[TestClass]
public class AnalyzeCommandSettingsSpecs
{
    [TestMethod]
    public void Bare_report_risk_flag_enables_risk_reporting_without_an_explicit_path()
    {
        var settings = new AnalyzeCommandSettings
        {
            ReportRisk = true
        };

        settings.GetReportRiskPath().Should().BeNull();
        settings.ToCoreSettings().ReportRisk.Should().BeTrue();
    }
}
