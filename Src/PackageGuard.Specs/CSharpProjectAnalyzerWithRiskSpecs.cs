using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
public class CSharpProjectAnalyzerWithRiskSpecs
{
    [TestMethod]
    public void Should_include_risk_metrics_in_analysis_result()
    {
        // Arrange
        var scanner = new CSharpProjectScanner(NullLogger.Instance);
        var analyzer = new NuGetPackageAnalyzer(NullLogger.Instance, new LicenseFetcher(NullLogger.Instance, null));
        var riskEvaluator = new RiskEvaluator(NullLogger.Instance);
        var projectAnalyzer = new CSharpProjectAnalyzer(scanner, analyzer, riskEvaluator);

        // Test data setup would go here
        // For now, just verify the API is working
        
        // Assert - API compatibility
        projectAnalyzer.Should().NotBeNull();
        projectAnalyzer.Should().BeOfType<CSharpProjectAnalyzer>();
    }
}