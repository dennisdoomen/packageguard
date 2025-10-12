namespace PackageGuard.Core.Npm;

/// <summary>
/// Responsible for detecting the package manager
/// used in a Node.js project, based on the executable name provided in the settings.
/// </summary>
/// <remarks>
/// This detector uses the name of the executable file (e.g., npm.exe, yarn.ps1, pnpm.exe)
/// provided in the <see cref="AnalyzerSettings.NpmExePatch"/> to identify the corresponding
/// package manager. The identified package manager is then set in the settings.
/// If no executable name matches, the detection process fails and returns false.
/// </remarks>
internal class ProvidedExeNameDetector : IDetectPackageManager
{
    public bool Detect(string projectOrSolutionPath, AnalyzerSettings settings)
    {
        if (settings.NpmExePatch is not null)
        {
            var mappings = new List<(string executable, NpmPackageManager manager)>
            {
                // TODO: Check the names of the executables on Windows and Linux
                ("npm.cmd", NpmPackageManager.Npm),
                ("npm.exe", NpmPackageManager.Npm),
                ("yarn.ps1", NpmPackageManager.Yarn),
                ("pnpm.exe", NpmPackageManager.Pnpm)
            };

            (string executable, NpmPackageManager manager)? mapping = mappings
                .FirstOrDefault(m => settings.NpmExePatch.EndsWith(m.executable, StringComparison.OrdinalIgnoreCase));

            if (mapping is not null)
            {
                settings.NpmPackageManager = mapping.Value.manager;
                return true;
            }
        }

        return false;
    }
}
