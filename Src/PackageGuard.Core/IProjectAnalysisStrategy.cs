namespace PackageGuard.Core;

internal interface IProjectAnalysisStrategy
{
    Task<PolicyViolation[]> ExecuteAnalysis(string projectOrSolutionPath, AnalyzerSettings settings,
        PackageInfoCollection packages);
}
