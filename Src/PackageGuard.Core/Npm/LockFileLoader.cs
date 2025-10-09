using CliWrap;
using Microsoft.Extensions.Logging;
using Pathy;

namespace PackageGuard.Core.Npm;

internal class LockFileLoader(ILogger logger)
{
    private readonly Dictionary<NpmPackageManager, string> lockFileByManager = new()
    {
        { NpmPackageManager.Npm, "package-lock.json" },
        { NpmPackageManager.Yarn, "yarn.lock" },
        { NpmPackageManager.Pnpm, "pnpm-lock.yaml" }
    };

    private readonly Dictionary<NpmPackageManager, (string windows, string linux)> exeByManager = new()
    {
        { NpmPackageManager.Npm, ("npm", "npm") },
        { NpmPackageManager.Yarn, ("yarn.cmd", "yarn") },
        { NpmPackageManager.Pnpm, ("pnpm.cmd", "pnpm") }
    };

    public ChainablePath GetPackageLockFile(ChainablePath packageJsonPath, NpmPackageManager manager, AnalyzerSettings settings)
    {
        ChainablePath lockFile = packageJsonPath.Parent / lockFileByManager[manager];
        DateTime lastProjectModification = File.GetLastWriteTimeUtc(packageJsonPath);

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
