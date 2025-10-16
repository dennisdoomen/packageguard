using Pathy;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Detects the presence of specific files or configurations that indicate the use of a particular
/// Npm-based package manager (e.g., Npm, Yarn, Pnpm) within a given project or solution directory.
/// </summary>
internal class CommonFileDetector : IDetectPackageManager
{
    public bool Detect(string projectOrSolutionPath, AnalyzerSettings settings)
    {
        var mappings = new List<(string lockFile, NpmPackageManager manager)>
        {
            ( "package-lock.json", NpmPackageManager.Npm ),
            ( ".npmrc", NpmPackageManager.Npm ),
            ( "pnpm-lock.yaml", NpmPackageManager.Pnpm ),
            ( "pnpm-workspace.yml", NpmPackageManager.Pnpm ),
            ( "yarn.lock", NpmPackageManager.Yarn ),
            ( ".yarnrc.yml", NpmPackageManager.Yarn ),
            ( ".yarnrc", NpmPackageManager.Yarn ),
            ( "package.json", NpmPackageManager.Npm ),
        };

        ChainablePath path = projectOrSolutionPath.ToPath();

        foreach ((string lockFile, NpmPackageManager manager) in mappings)
        {
            if (path.Name.Equals(lockFile, StringComparison.OrdinalIgnoreCase) && path.FileExists)
            {
                settings.NpmPackageManager = manager;
                return true;
            }

            if ((path / lockFile).FileExists)
            {
                settings.NpmPackageManager = manager;
                return true;
            }
        }

        return false;
    }
}
