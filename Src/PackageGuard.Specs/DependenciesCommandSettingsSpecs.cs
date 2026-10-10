using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.CSharp;

namespace PackageGuard.Specs;

[TestClass]
public class DependenciesCommandSettingsSpecs
{
    [TestMethod]
    public void Does_not_fail_or_report_conflicts_unless_asked_to()
    {
        var settings = new DependenciesCommandSettings();

        settings.Severity.Should().Be(DependencySeverity.Warning);
        settings.IncludeConflicts.Should().BeFalse();
    }

    [TestMethod]
    public void Reports_redundant_references_without_the_conflicts_option()
    {
        var settings = new DependenciesCommandSettings();

        settings.IsReported(new DependencyFinding { Kind = DependencyFindingKind.RedundantViaProject }).Should().BeTrue();
        settings.IsReported(new DependencyFinding { Kind = DependencyFindingKind.RedundantViaPackage }).Should().BeTrue();
    }

    [TestMethod]
    public void Hides_version_conflicts_until_asked_for_so_they_never_affect_the_exit_code()
    {
        var finding = new DependencyFinding { Kind = DependencyFindingKind.VersionConflict };

        new DependenciesCommandSettings().IsReported(finding).Should().BeFalse();
        new DependenciesCommandSettings { IncludeConflicts = true }.IsReported(finding).Should().BeTrue();
    }
}
