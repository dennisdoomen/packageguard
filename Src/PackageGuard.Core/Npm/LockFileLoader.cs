using CliWrap;
using Microsoft.Extensions.Logging;
using Pathy;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Provides functionality to load lock files associated with a given npm project.
/// </summary>
internal class LockFileLoader(ILogger logger)
{
    /// <summary>
    /// Retrieves the lock file path for an npm project based on the specified package manager and settings.
    /// If required, restores project packages to ensure the lock file is updated.
    /// </summary>
    public ChainablePath GetPackageLockFile(ChainablePath packageJsonPath, NpmPackageManager manager, AnalyzerSettings settings)
    {
        Dictionary<NpmPackageManager, string> lockFileByManager = new()
        {
            { NpmPackageManager.Npm, "package-lock.json" },
            { NpmPackageManager.Yarn, "yarn.lock" },
            { NpmPackageManager.Pnpm, "pnpm-lock.yaml" }
        };

        ChainablePath lockFile = packageJsonPath.Parent / lockFileByManager[manager];
        DateTime lastProjectModification = packageJsonPath.LastWriteTimeUtc;

        FileInfo lockFileInfo = new(lockFile);

        if (!settings.SkipRestore &&
            (settings.ForceRestore || !lockFile.Exists || lockFileInfo.LastWriteTimeUtc < lastProjectModification))
        {
            RestoreProjectPackages(packageJsonPath.Parent, manager); // Call the RestoreProjectPackages method
        }

        return lockFile;
    }

    private void RestoreProjectPackages(ChainablePath projectPath, NpmPackageManager manager)
    {
        logger.LogInformation("The lock file was not found or out-of-date. Running install on {Path} ", projectPath);

        Dictionary<NpmPackageManager, (string windows, string linux)> exeByManager = new()
        {
            { NpmPackageManager.Npm, ("npm", "npm") },
            { NpmPackageManager.Yarn, ("yarn", "yarn") },
            { NpmPackageManager.Pnpm, ("pnpm", "pnpm") }
        };

        string executable = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? exeByManager[manager].windows
            : exeByManager[manager].linux;

        Command cli = Cli.Wrap(executable)
            .WithWorkingDirectory(projectPath)
            .WithArguments("install")
            .WithStandardOutputPipe(PipeTarget.ToDelegate(msg => logger.LogInformation(msg)))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(msg => logger.LogError(msg)));

        logger.LogInformation("Executing: {Cli}", cli);

        CommandResult result = cli.ExecuteAsync().GetAwaiter().GetResult();

        if (!result.IsSuccess)
        {
            throw new ApplicationException(
                $"Failed to install the {manager} dependencies for {projectPath} with {result.ExitCode}");
        }
    }
}
