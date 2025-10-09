namespace PackageGuard.Core.Npm;

/// <summary>
/// Interface for detecting the package manager used within a specified project or solution directory.
/// </summary>
internal interface IDetectPackageManager
{
    bool Detect(string projectOrSolutionPath, AnalyzerSettings settings);
}
