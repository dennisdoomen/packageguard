using CliWrap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.ProjectModel;
using Pathy;

namespace PackageGuard.Core;

internal class DotNetLockFileLoader
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

    public LockFile? GetPackageLockFile(ChainablePath projectPath)
    {
        Logger.LogInformation("Loading lock file for {ProjectPath}", projectPath);

        var lastProjectModification = File.GetLastWriteTimeUtc(projectPath);

        FileInfo assetsJson = new( projectPath.Directory / "obj" / "project.assets.json");

        if (ForceRestore || !assetsJson.Exists || assetsJson.LastWriteTimeUtc < lastProjectModification)
        {
            string interactiveFlag = InteractiveRestore ? "--interactive" : "";

            Logger.LogInformation("Project.assets.json not found or out-of-date. Running restore {Flag} on {Path} ",
                interactiveFlag, projectPath);

            var result = Cli.Wrap("dotnet")
                .WithArguments($"restore {projectPath} {interactiveFlag}")
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

}
