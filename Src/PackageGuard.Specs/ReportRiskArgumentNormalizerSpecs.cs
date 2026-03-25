using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PackageGuard.Specs;

[TestClass]
public class ReportRiskArgumentNormalizerSpecs
{
    [TestMethod]
    public void Normalize_keeps_bare_report_risk_flag_without_extracting_a_path()
    {
        var result = ReportRiskArgumentNormalizer.Normalize(["Test.sln", "--report-risk", "--ignore-violations"]);

        result.Args.Should().Equal("Test.sln", "--report-risk", "--ignore-violations");
        result.ReportRiskPath.Should().BeNull();
    }

    [TestMethod]
    public void Normalize_extracts_a_path_following_report_risk()
    {
        var result = ReportRiskArgumentNormalizer.Normalize(["Test.sln", "--report-risk", @"C:\temp\risk.html", "--ignore-violations"]);

        result.Args.Should().Equal("Test.sln", "--report-risk", "--ignore-violations");
        result.ReportRiskPath.Should().Be(@"C:\temp\risk.html");
    }

    [TestMethod]
    public void Normalize_extracts_an_inline_report_risk_path()
    {
        var result = ReportRiskArgumentNormalizer.Normalize(["Test.sln", "--report-risk=C:\\temp\\risk.html", "--ignore-violations"]);

        result.Args.Should().Equal("Test.sln", "--report-risk", "--ignore-violations");
        result.ReportRiskPath.Should().Be(@"C:\temp\risk.html");
    }
}
