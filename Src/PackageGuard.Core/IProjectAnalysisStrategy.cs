using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;

namespace PackageGuard.Core;

internal interface IProjectAnalysisStrategy
{
    Task<PolicyViolation[]> ExecuteAnalysis(string projectOrSolutionPath, AnalyzerSettings settings,
        PackageInfoCollection packages);
}
