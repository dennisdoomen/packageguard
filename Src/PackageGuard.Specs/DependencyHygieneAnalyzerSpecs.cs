using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.CSharp;

namespace PackageGuard.Specs;

[TestClass]
public class DependencyHygieneAnalyzerSpecs
{
    [TestMethod]
    public void Can_find_a_package_that_a_referenced_project_already_provides()
    {
        // Arrange
        var project = new LockFileBuilder("Api")
            .WithProjectReference("Domain", provides: ("Serilog", "3.1.1"))
            .WithPackageReference("Serilog", "3.1.1")
            .Build();

        // Act
        IReadOnlyCollection<DependencyFinding> findings = new DependencyHygieneAnalyzer().Analyze([project]);

        // Assert
        findings.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind = DependencyFindingKind.RedundantViaProject,
                Project = "Api",
                PackageId = "Serilog",
                ResolvedVersion = "3.1.1",
                SafeToRemove = true
            });
    }

    [TestMethod]
    public void Can_find_a_package_that_another_package_already_provides()
    {
        // Arrange
        var project = new LockFileBuilder("Api")
            .WithPackageReference("Serilog.Sinks.Console", "5.0.0", dependsOn: ("Serilog", "3.1.1"))
            .WithPackageReference("Serilog", "3.1.1")
            .Build();

        // Act
        IReadOnlyCollection<DependencyFinding> findings = new DependencyHygieneAnalyzer().Analyze([project]);

        // Assert
        findings.Should().ContainSingle()
            .Which.Kind.Should().Be(DependencyFindingKind.RedundantViaPackage);
    }

    [TestMethod]
    public void Ignores_a_package_that_is_only_there_at_build_time()
    {
        // Arrange
        // A PrivateAssets="all" package is not a dependency of what the project ships, so it must never
        // be treated as the thing that provides another reference.
        var project = new LockFileBuilder("Api")
            .WithPackageReference("SomeAnalyzer", "1.0.0", dependsOn: ("Serilog", "3.1.1"), privateAssets: true)
            .WithPackageReference("Serilog", "3.1.1")
            .Build();

        // Act
        IReadOnlyCollection<DependencyFinding> findings = new DependencyHygieneAnalyzer().Analyze([project]);

        // Assert
        findings.Should().BeEmpty();
    }

    [TestMethod]
    public void Ignores_an_implicitly_referenced_package()
    {
        // Arrange
        var project = new LockFileBuilder("Api")
            .WithPackageReference("Implicit.Sdk.Package", "1.0.0", dependsOn: ("Serilog", "3.1.1"), autoReferenced: true)
            .WithPackageReference("Serilog", "3.1.1")
            .Build();

        // Act
        IReadOnlyCollection<DependencyFinding> findings = new DependencyHygieneAnalyzer().Analyze([project]);

        // Assert
        findings.Should().BeEmpty();
    }

    [TestMethod]
    public void Marks_a_reference_that_keeps_the_version_up_as_needing_review()
    {
        // Arrange
        // The project asks for 3.1.1 while the other package only asks for 3.0.0, so removing the direct
        // reference would silently downgrade it.
        var project = new LockFileBuilder("Api")
            .WithPackageReference("Serilog.Sinks.Console", "5.0.0", dependsOn: ("Serilog", "3.0.0"))
            .WithPackageReference("Serilog", "3.1.1")
            .Build();

        // Act
        IReadOnlyCollection<DependencyFinding> findings = new DependencyHygieneAnalyzer().Analyze([project]);

        // Assert
        DependencyFinding finding = findings.Should().ContainSingle().Subject;
        finding.SafeToRemove.Should().BeFalse();
        finding.WouldResolveTo.Should().Be("3.0.0");
        finding.Description.Should().Contain("lowers Serilog from 3.1.1 to 3.0.0");
    }

    [TestMethod]
    public void Can_find_a_package_resolving_to_different_versions_across_projects()
    {
        // Arrange
        var api = new LockFileBuilder("Api").WithPackageReference("Serilog", "3.1.1").Build();
        var worker = new LockFileBuilder("Worker").WithPackageReference("Serilog", "2.12.0").Build();

        // Act
        IReadOnlyCollection<DependencyFinding> findings = new DependencyHygieneAnalyzer().Analyze([api, worker]);

        // Assert
        DependencyFinding conflict = findings.Should().ContainSingle(x => x.Kind == DependencyFindingKind.VersionConflict)
            .Subject;

        conflict.PackageId.Should().Be("Serilog");
        conflict.ResolvedVersion.Should().Be("3.1.1", "the highest conflicting version is reported first");
        conflict.Providers.Should().Equal("3.1.1", "2.12.0");
    }

    [TestMethod]
    public void Does_not_report_a_conflict_when_the_frameworks_differ()
    {
        // Arrange
        var api = new LockFileBuilder("Api", "net9.0").WithPackageReference("Serilog", "3.1.1").Build();
        var legacy = new LockFileBuilder("Legacy", "net8.0").WithPackageReference("Serilog", "2.12.0").Build();

        // Act
        IReadOnlyCollection<DependencyFinding> findings = new DependencyHygieneAnalyzer().Analyze([api, legacy]);

        // Assert
        findings.Should().NotContain(x => x.Kind == DependencyFindingKind.VersionConflict);
    }

    [TestMethod]
    public void Can_exclude_a_package_from_the_results()
    {
        // Arrange
        var project = new LockFileBuilder("Api")
            .WithProjectReference("Domain", provides: ("Serilog", "3.1.1"))
            .WithPackageReference("Serilog", "3.1.1")
            .Build();

        var analyzer = new DependencyHygieneAnalyzer
        {
            ExcludedPackageIds = ["serilog"]
        };

        // Act
        IReadOnlyCollection<DependencyFinding> findings = analyzer.Analyze([project]);

        // Assert
        findings.Should().BeEmpty("the exclusion is not case sensitive");
    }

    [TestMethod]
    public void Returns_nothing_when_every_reference_is_needed()
    {
        // Arrange
        var project = new LockFileBuilder("Api")
            .WithPackageReference("Serilog", "3.1.1")
            .WithPackageReference("Dapper", "2.1.35")
            .Build();

        // Act
        IReadOnlyCollection<DependencyFinding> findings = new DependencyHygieneAnalyzer().Analyze([project]);

        // Assert
        findings.Should().BeEmpty();
    }
}
