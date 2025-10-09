using Microsoft.Extensions.Logging;
using Pathy;

namespace PackageGuard.Core.Npm;

public class NpmProjectAnalysisStrategy(ILogger logger) : IProjectAnalysisStrategy
{
    public Task<PolicyViolation[]> ExecuteAnalysis(string projectOrSolutionPath, AnalyzerSettings settings,
        PackageInfoCollection getPolicyByProject)
    {
        List<PolicyViolation> violations = new();

        DetectPackageManager(projectOrSolutionPath, settings);
        ChainablePath packageJsonPath = projectOrSolutionPath.ToPath();

        if (settings.NpmPackageManager == NpmPackageManager.Npm)
        {
            // REFACTOR: Extract in an extension method ResolveFile("fileName") in Pathy
            if (!packageJsonPath.FileExists)
            {
                packageJsonPath /= "package.json";
                if (packageJsonPath.FileExists)
                {
                    var loader = new LockFileLoader(logger);
                    var lockFile = loader.GetPackageLockFile(packageJsonPath, settings.NpmPackageManager.Value, settings);
                }
            }
        }

        return Task.FromResult(violations.ToArray());
    }

    private static void DetectPackageManager(string projectOrSolutionPath, AnalyzerSettings settings)
    {
        IDetectPackageManager[] detectors = {
            new UsingUserProvidedSettingsDetector(),
            new ProvidedExeNameDetector(),
            new CommonFileDetector()
        };

        foreach (IDetectPackageManager detector in detectors)
        {
            if (detector.Detect(projectOrSolutionPath, settings))
            {
                break;
            }
        }
    }
}
