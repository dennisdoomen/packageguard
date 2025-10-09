namespace PackageGuard.Core.Npm;

/// <summary>
///
/// </summary>
internal interface IDetectPackageManager
{
    bool Detect(string projectOrSolutionPath, AnalyzerSettings settings);
}
