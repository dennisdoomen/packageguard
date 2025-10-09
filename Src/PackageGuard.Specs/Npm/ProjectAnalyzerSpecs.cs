using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using PackageGuard.Specs.Common;
using Pathy;

namespace PackageGuard.Specs.Npm;

[TestClass]
public class ProjectAnalyzerSpecs
{
    private readonly LicenseFetcher licenseFetcher =
        new(NullLogger.Instance, Environment.GetEnvironmentVariable("GITHUB_API_KEY"));

    [TestMethod]
    public async Task Runs_install_to_get_the_missing_lock_file()
    {
        // Arrange
        var project = ChainablePath.Current / "TestCases" / "NpmAppWithoutLockFile";
        project.GlobFiles("*-lock.json", "node_modules\\**").ForEach(f => f.DeleteFileOrDirectory());

        // Act
        var analyzer = new ProjectAnalyzer(licenseFetcher)
        {
            Logger = ConsoleTestLogger.Create("Test")
        };

        // Act
        await analyzer.ExecuteAnalysis(project, new AnalyzerSettings(), _ => new ProjectPolicy());

        // Assert

    }
}
