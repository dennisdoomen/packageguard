using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meziantou.Extensions.Logging.InMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Npm;
using PackageGuard.Core.Policy;

namespace PackageGuard.Specs;

[TestClass]
public class NuGetSourceWarningSpecs
{
    private string directory;
    private string projectPath;

    [TestInitialize]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(directory, "obj"));
        projectPath = Path.Combine(directory, "test.csproj");
        File.WriteAllText(projectPath, """<Project Sdk="Microsoft.NET.Sdk" />""");
        File.WriteAllText(Path.Combine(directory, "obj", "project.assets.json"),
            """{"version":3,"targets":{},"libraries":{},"project":{"frameworks":{}}}""");
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"), """
            <configuration><packageSources><clear />
              <add key="public" value="https://api.nuget.org/v3/index.json" />
              <add key="private" value="https://private.example.test/v3/index.json" />
            </packageSources></configuration>
            """);
    }

    [TestCleanup]
    public void CleanUp() => Directory.Delete(directory, true);

    [TestMethod]
    [DataRow(false, false, true, false, 0)]
    [DataRow(true, false, true, false, 1)]
    [DataRow(true, true, true, false, 0)]
    [DataRow(true, false, false, false, 0)]
    [DataRow(true, false, true, true, 1)]
    public async Task Respects_project_setting_ignore_violations_and_disabled_nuget(
        bool fail, bool ignore, bool scan, bool denyAsWarning, int expected)
    {
        string configPath = Path.Combine(directory, "packageguard.config.json");
        await File.WriteAllTextAsync(configPath,
            "{\"settings\":{\"failOnNugetSourceWarnings\":" + fail.ToString().ToLowerInvariant() +
            ",\"allow\":{\"licenses\":[\"MIT\"]}}}");
        var provider = new InMemoryLoggerProvider();
        var command = new AnalyzeCommand(provider.CreateLogger(""));
        var settings = new AnalyzeCommandSettings
        {
            ProjectPath = projectPath,
            ConfigPath = configPath,
            SkipRestore = true,
            IgnoreViolations = ignore,
            ScanNuGet = scan,
            TreatDenyAsWarning = denyAsWarning,
            NpmPackageManager = NpmPackageManager.None
        };

        int exitCode = await command.ExecuteAsync(null!, settings, CancellationToken.None);

        exitCode.Should().Be(expected);
        var sourceMessages = provider.Logs.Where(entry => entry.Message.Contains("may supply the same")).ToArray();
        sourceMessages.Should().HaveCount(scan ? 1 : 0);
        if (scan)
        {
            sourceMessages[0].Message.Should().Contain("private").And.Contain("NuGet.Config")
                .And.Contain("https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping");
        }
    }

    [TestMethod]
    public void Deduplicates_findings_but_keeps_failure_from_any_affected_project()
    {
        var provider = new InMemoryLoggerProvider();
        var reporter = new NuGetSourceWarningReporter(provider.CreateLogger(""));
        reporter.CheckProject(projectPath, new ProjectPolicy());
        string second = Path.Combine(directory, "another.csproj");
        reporter.CheckProject(second, new ProjectPolicy { FailOnNugetSourceWarnings = true });

        reporter.Complete().Should().BeTrue();
        reporter.FailingProjects.Should().BeEquivalentTo(second);
        provider.Logs.Count(entry => entry.Message.Contains("may supply the same")).Should().Be(1);
        provider.Logs.Select(entry => entry.Message).Should().Contain(message => message.Contains(projectPath) && message.Contains(second));
    }
}
