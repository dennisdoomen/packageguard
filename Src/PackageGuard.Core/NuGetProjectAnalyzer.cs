using CliWrap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.ProjectModel;

namespace PackageGuard.Core;

public class NuGetProjectAnalyzer(ProjectScanner scanner, NuGetPackageAnalyzer analyzer)
{
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// A path to a folder containing a solution or project file. If it points to a solution, all the projects in that
    /// solution are included. If it points to a directory with more than one solution, it will fail.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// If specified, a list of packages, versions and licenses that are allowed. Everything else is forbidden.
    /// </summary>
    /// <remarks>
    /// Can be overridden by <see cref="DenyList"/>
    /// </remarks>
    public AllowList AllowList { get; set; } = new();

    /// <summary>
    /// If specified, a list of packages, versions and licenses that are forbidden, even if it was listed in <see cref="AllowList"/>.
    /// </summary>
    public DenyList DenyList { get; set; } = new();

    public async Task<PolicyViolation[]> ExecuteAnalysis()
    {
        if (!AllowList.HasPolicies && !DenyList.HasPolicies)
        {
            throw new ArgumentException("Either a allowlist or a denylist must be specified");
        }

        List<string> projectPaths = scanner.FindProjects(ProjectPath);

        PackageInfoCollection packages = new();
        foreach (var projectPath in projectPaths)
        {
            Logger.LogHeader($"Getting metadata for packages in {projectPath}");

            LockFile? lockFile = GetPackageLockFile(projectPath);
            if (lockFile is not null)
            {
                foreach (LockFileLibrary? library in lockFile.Libraries.Where(library => library.Type == "package"))
                {
                    await analyzer.CollectPackageMetadata(projectPath, library.Name, library.Version, packages);
                }
            }
        }

        return VerifyAgainstPolicy(packages);
    }

    private LockFile? GetPackageLockFile(string projectPath)
    {
        Logger.LogInformation("Loading lock file for {ProjectPath}", projectPath);

        var lastProjectModification = File.GetLastWriteTimeUtc(projectPath);

        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        FileInfo assetsJson = new(Path.Combine(projectDirectory!, "obj", "project.assets.json"));

        if (!assetsJson.Exists || assetsJson.LastWriteTimeUtc < lastProjectModification)
        {
            Logger.LogInformation("Project.assets.json not found or out-of-date. Running restore on {Path} ", projectPath);

            var result = Cli.Wrap("dotnet")
                .WithArguments($"restore {projectPath} --interactive")
                .WithStandardOutputPipe(PipeTarget.ToStream(Console.OpenStandardOutput()))
                .WithStandardErrorPipe(PipeTarget.ToStream(Console.OpenStandardError()))
                .ExecuteAsync().GetAwaiter().GetResult();

            if (!result.IsSuccess)
            {
                throw new ApplicationException($"Failed to restore the dependencies for {projectPath} with {result.ExitCode}");
            }
        }

        LockFile lockFile = LockFileUtilities.GetLockFile(assetsJson.FullName, new NuGet.Common.NullLogger());
        if (lockFile == null)
        {
            Logger.LogWarning("Failed to load the lock file for {ProjectPath} so skipping it", projectPath);
        }

        return lockFile;
    }

    private PolicyViolation[] VerifyAgainstPolicy(PackageInfoCollection packages)
    {
        var violations = new List<PolicyViolation>();

        foreach (PackageInfo package in packages)
        {
            if (!AllowList.Complies(package) || !DenyList.Complies(package))
            {
                violations.Add(new PolicyViolation(package.Id, package.Version, package.License!, package.Projects.ToArray()));
            }
        }

        return violations.ToArray();
    }
}
