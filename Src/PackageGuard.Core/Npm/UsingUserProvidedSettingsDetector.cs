namespace PackageGuard.Core.Npm;

/// <summary>
/// Detects whether the specified project or solution uses user-provided settings
/// to determine the package manager for a Node.js project.
/// </summary>
/// <remarks>
/// This detector verifies if the user has explicitly defined the npm package manager
/// in the provided settings or if the package manager is set to "None". If either condition
/// is met, the detector will return true.
/// </remarks>
internal class UsingUserProvidedSettingsDetector : IDetectPackageManager
{
    public bool Detect(string projectOrSolutionPath, AnalyzerSettings settings)
    {
        return settings.NpmPackageManager.HasValue || settings.NpmPackageManager == NpmPackageManager.None;
    }
}
