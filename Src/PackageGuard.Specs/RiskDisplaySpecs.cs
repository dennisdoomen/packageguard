using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PackageGuard.Specs;

[TestClass]
public class RiskDisplaySpecs
{
    [TestMethod]
    [DataRow(0.0, "green3_1")]
    [DataRow(29.9, "green3_1")]
    [DataRow(30.0, "yellow1")]
    [DataRow(59.9, "yellow1")]
    [DataRow(60.0, "red1")]
    [DataRow(100.0, "red1")]
    public void Maps_a_risk_score_to_the_expected_color(double score, string expectedColor)
    {
        RiskDisplay.GetRiskColor(score).Should().Be(expectedColor);
    }

    [TestMethod]
    [DataRow(0.0, "Low")]
    [DataRow(29.9, "Low")]
    [DataRow(30.0, "Medium")]
    [DataRow(59.9, "Medium")]
    [DataRow(60.0, "High")]
    [DataRow(100.0, "High")]
    public void Maps_a_risk_score_to_the_expected_zone(double score, string expectedZone)
    {
        RiskDisplay.GetRiskZone(score).Should().Be(expectedZone);
    }

    [TestMethod]
    [DataRow(0, "0.0")]
    [DataRow(12.3, "12.3")]
    [DataRow(12.34, "12.3")]
    [DataRow(12.36, "12.4")]
    [DataRow(100, "100.0")]
    public void Formats_a_score_to_one_decimal_place_using_invariant_culture(double value, string expected)
    {
        RiskDisplay.FormatDecimal(value).Should().Be(expected);
    }
}
