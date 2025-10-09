using CliWrap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.ProjectModel;
using Pathy;

namespace PackageGuard.Core.CSharp;

public class DotNetLockFileLoader
{
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Specifies whether interactive mode should be enabled for the .NET restore process.
    /// When enabled, the restore operation may prompt for user input, such as authentication information.
    /// </summary>
    /// <value>
    /// Defaults to <c>true</c>.
    /// </value>
    public bool InteractiveRestore { get; set; } = true;

    /// <summary>
    /// Force restoring the NuGet dependencies, even if the lockfile is up-to-date
    /// </summary>
    public bool ForceRestore { get; set; } = false;

    /// <summary>
    /// Determines whether to skip the restore operation for the project analysis.
    /// If set to true, no project or solution restore will be performed before analyzing the project dependencies.
    /// </summary>
    public bool SkipRestore { get; set; }

    public LockFile? GetPackageLockFile(string path)
    {
        ChainablePath projectPath = path;

        Logger.LogInformation("Loading lock file for {ProjectPath}", projectPath);

        DateTime lastProjectModification = File.GetLastWriteTimeUtc(projectPath);

        FileInfo assetsJson = new(projectPath.Directory / "obj" / "project.assets.json");

        if (!SkipRestore && (ForceRestore || !assetsJson.Exists || assetsJson.LastWriteTimeUtc < lastProjectModification))
        {
            RestoreProjectPackages(projectPath);
        }

        LockFile lockFile = LockFileUtilities.GetLockFile(assetsJson.FullName, new NuGet.Common.NullLogger());
        if (lockFile == null)
        {
            Logger.LogWarning("Failed to load the lock file for {ProjectPath} so skipping it", projectPath);
        }

        return lockFile;
    }

    private void RestoreProjectPackages(ChainablePath projectPath)
    {
        Logger.LogInformation("Project.assets.json not found or out-of-date. Running restore on {Path} ", projectPath);

        var arguments = new List<string>
        {
            "restore",
            projectPath
        };

        if (InteractiveRestore)
        {
            arguments.Add("--interactive");
        }

        Command cli = Cli.Wrap("dotnet")
            .WithArguments(arguments)
            .WithStandardOutputPipe(PipeTarget.ToStream(Console.OpenStandardOutput()))
            .WithStandardErrorPipe(PipeTarget.ToStream(Console.OpenStandardError()));

        Logger.LogInformation("Executing: {Cli}", cli);

        CommandResult result = cli.ExecuteAsync().GetAwaiter().GetResult();

        if (!result.IsSuccess)
        {
            throw new ApplicationException($"Failed to restore the dependencies for {projectPath} with {result.ExitCode}");
        }
    }
}
