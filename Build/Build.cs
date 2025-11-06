using System;
using System.IO.Compression;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Coverlet;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.ReportGenerator;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.ReportGenerator.ReportGeneratorTasks;
using static Serilog.Log;

class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode
    public static int Main() => Execute<Build>(x => x.Default);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    GitHubActions GitHubActions => GitHubActions.Instance;

    string BranchSpec => GitHubActions?.Ref;

    string BuildNumber => GitHubActions?.RunNumber.ToString();

    [Parameter("The key to push to Nuget")]
    [Secret]
    readonly string NuGetApiKey;

    [Parameter("The key to use for scanning packages on GitHub")]
    [Secret]
    readonly string GitHubApiKey;

    [Solution(GenerateProjects = true)]
    readonly Solution Solution;

    [GitVersion(Framework = "net8.0", NoFetch = true, NoCache = true)]
    readonly GitVersion GitVersion;

    [NuGetPackage("JetBrains.ReSharper.GlobalTools", "inspectcode.exe")]
    Tool InspectCode;

    string Value;

    AbsolutePath ArtifactsDirectory => RootDirectory / "Artifacts";

    AbsolutePath TestResultsDirectory => RootDirectory / "TestResults";

    string SemVer;

    Target CalculateNugetVersion => _ => _
        .Executes(() =>
        {
            SemVer = GitVersion?.SemVer ?? "0.0.0-dev";
            if (IsPullRequest)
            {
                Information(
                    "Branch spec {branchspec} is a pull request. Adding build number {buildnumber}",
                    BranchSpec, BuildNumber);

                SemVer = string.Join('.', SemVer.Split('.').Take(3).Union(new[]
                {
                    BuildNumber
                }));
            }

            Information("SemVer = {semver}", SemVer);
        });

    Target Compile => _ => _
        .DependsOn(CalculateNugetVersion)
        .Executes(() =>
        {
            ReportSummary(s => s
                .WhenNotNull(SemVer, (summary, semVer) => summary
                    .AddPair("Version", semVer)));

            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetVersion(SemVer)
                .SetAssemblyVersion(GitVersion?.AssemblySemVer ?? SemVer)
                .SetFileVersion(GitVersion?.AssemblySemFileVer ?? SemVer)
                .SetInformationalVersion(GitVersion?.InformationalVersion ?? SemVer));
        });

    Target RunInspectCode => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            InspectCode($"PackageGuard.sln -o={ArtifactsDirectory / "CodeIssues.sarif"} --no-build");
        });

    Target RunTests => _ => _
        .DependsOn(Compile, RunInspectCode)
        .Executes(() =>
        {
            TestResultsDirectory.CreateOrCleanDirectory();
            var project = Solution.PackageGuard_Specs;

            DotNetTest(s => s
                // We run tests in debug mode so that Fluent Assertions can show the names of variables
                .SetConfiguration(Configuration.Debug)
                .SetDataCollector("XPlat Code Coverage")
                .EnableCollectCoverage()
                .SetResultsDirectory(TestResultsDirectory)
                .SetProjectFile(project)
                .WhenNotNull(GitHubApiKey, (ss, key) => ss
                    .SetProcessEnvironmentVariable("GITHUB_API_KEY", key)
                )
                .CombineWith(project.GetTargetFrameworks(),
                    (ss, framework) => ss
                        .SetFramework(framework)
                        .AddLoggers($"trx;LogFileName={project!.Name}_{framework}.trx")
                ));
        });

    Target ApiChecks => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var project = Solution.PackageGuard_ApiVerificationTests;

            DotNetTest(s => s
                .SetConfiguration(Configuration)
                .SetProcessEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", "en-US")
                .SetResultsDirectory(TestResultsDirectory)
                .SetProjectFile(project)
                .AddLoggers($"trx;LogFileName={project!.Name}.trx"));
        });

    Target RunPackageGuard => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var project = Solution.PackageGuard;

            Configure<DotNetRunSettings> configurator = s => s
                .SetProjectFile(project)
                .SetConfiguration(Configuration)

                .WhenNotNull(GitHubApiKey, (ss, key) => ss
                    .AddApplicationArguments($"--github-api-key={key}")
                    .AddProcessRedactedSecrets(key))
                .AddApplicationArguments("--use-caching")
                .AddApplicationArguments($"{RootDirectory}")
                .EnableNoBuild()
                .EnableNoRestore();

            Information("Running PackageGuard without explicit config path:");
            DotNetRun(configurator);

            Information("Running PackageGuard with explicit config path:");
            DotNetRun(s => configurator(s)
                .AddApplicationArguments($"--configpath={RootDirectory / ".packageguard" / "config.json"}"));
        });

    Target CodeCoverage => _ => _
        .DependsOn(RunTests)
        .Executes(() =>
        {
            ReportGenerator(s => s
                .SetTargetDirectory(TestResultsDirectory / "reports")
                .AddReports(TestResultsDirectory / "**/coverage.cobertura.xml")
                .AddReportTypes(ReportTypes.lcov, ReportTypes.Html)
                .AddFileFilters("-*.g.cs")
                .AddFileFilters("-*.nuget*")
                .AddFileFilters("-*pathy*")
                .SetAssemblyFilters("+PackageGuard*"));

            string link = TestResultsDirectory / "reports" / "index.html";
            Information($"Code coverage report: \x1b]8;;file://{link.Replace('\\', '/')}\x1b\\{link}\x1b]8;;\x1b\\");
        });

    Target PreparePackageReadme => _ => _
        .Executes(() =>
        {
            var content = (RootDirectory / "README.md").ReadAllText();
            var sections = content.Split(["\n## "], StringSplitOptions.RemoveEmptyEntries);

            string[] headersToInclude =
            [
                "About",
                "How do I configure it",
                "How do I use it?",
                "Additional notes",
                "Versioning",
                "Credits"
            ];

            var readmeContent = "## " + string.Join("\n## ", sections
                .Where(section => headersToInclude.Any(header => section.StartsWith(header, StringComparison.OrdinalIgnoreCase))));

            (ArtifactsDirectory / "Readme.md").WriteAllText(readmeContent);
        });

    Target Pack => _ => _
        .DependsOn(CalculateNugetVersion)
        .DependsOn(ApiChecks)
        .DependsOn(CodeCoverage)
        .DependsOn(RunPackageGuard)
        .DependsOn(PreparePackageReadme)
        .Executes(() =>
        {
            ReportSummary(s => s
                .WhenNotNull(SemVer, (c, semVer) => c
                    .AddPair("Packed version", semVer)));

            DotNetPack(s => s
                .SetProject(Solution.PackageGuard)
                .SetOutputDirectory(ArtifactsDirectory)
                .SetConfiguration(Configuration)
                .EnableNoLogo()
                .EnableNoRestore()
                .EnableContinuousIntegrationBuild() // Necessary for deterministic builds
                .SetVersion(SemVer));
        });

    Target PublishBinary => _ => _
        .DependsOn(CalculateNugetVersion)
        .Executes(() =>
        {
            var publishDirectory = ArtifactsDirectory / "publish";
            publishDirectory.CreateOrCleanDirectory();

            // Publish for win-x64 as a self-contained executable
            DotNetPublish(s => s
                .SetProject(Solution.PackageGuard)
                .SetConfiguration(Configuration)
                .SetRuntime("win-x64")
                .EnableSelfContained()
                .EnablePublishSingleFile()
                .SetOutput(publishDirectory / "win-x64")
                .SetVersion(SemVer));

            // Create ZIP file
            var zipFileName = $"PackageGuard-{SemVer}-win-x64.zip";
            var zipFilePath = ArtifactsDirectory / zipFileName;
            
            Information($"Creating ZIP file: {zipFilePath}");
            ZipFile.CreateFromDirectory(publishDirectory / "win-x64", zipFilePath, CompressionLevel.Optimal, false);

            ReportSummary(s => s
                .AddPair("Binary ZIP", zipFileName));
        });

    Target Push => _ => _
        .DependsOn(Pack)
        .OnlyWhenDynamic(() => IsTag)
        .ProceedAfterFailure()
        .Executes(() =>
        {
            var packages = ArtifactsDirectory.GlobFiles("*.nupkg");

            Assert.NotEmpty(packages);

            DotNetNuGetPush(s => s
                .SetApiKey(NuGetApiKey)
                .EnableSkipDuplicate()
                .SetSource("https://api.nuget.org/v3/index.json")
                .EnableNoSymbols()
                .CombineWith(packages,
                    (v, path) => v.SetTargetPath(path)));
        });

    Target Default => _ => _
        .DependsOn(Push)
        .DependsOn(PublishBinary);

    bool IsPullRequest => GitHubActions?.IsPullRequest ?? false;

    bool IsTag => BranchSpec != null && BranchSpec.Contains("refs/tags", StringComparison.OrdinalIgnoreCase);
}
