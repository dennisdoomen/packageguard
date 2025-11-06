using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.CSharp;
using Pathy;

namespace PackageGuard.Specs.CSharp;

[TestClass]
public class CSharpProjectScannerSpecs
{
    private readonly CSharpProjectScanner cSharpProjectScanner = new(NullLogger.Instance);

    [TestMethod]
    public void Can_process_slnx_solution_files()
    {
        // Arrange & Act
        var slnxPath = ChainablePath.Current / "TestCases" / "SlnxApp" / "SlnxApp.slnx";
        var projects = cSharpProjectScanner.FindProjects(slnxPath);

        // Assert
        projects.Should().ContainSingle(p => p.EndsWith("SlnxApp.csproj"));
    }

    [TestMethod]
    public void Can_find_slnx_files_in_directory()
    {
        // Arrange & Act
        var directoryPath = ChainablePath.Current / "TestCases" / "SlnxApp";
        var projects = cSharpProjectScanner.FindProjects(directoryPath);

        // Assert
        projects.Should().ContainSingle(p => p.EndsWith("SlnxApp.csproj"));
    }
}
