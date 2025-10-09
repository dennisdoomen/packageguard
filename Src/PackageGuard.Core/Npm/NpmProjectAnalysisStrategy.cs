using Microsoft.Extensions.Logging;
using Pathy;

namespace PackageGuard.Core.Npm;

public class NpmProjectAnalysisStrategy(GetPolicyByProject policyByProject, ILogger logger) : IProjectAnalysisStrategy
{
    public async Task<PolicyViolation[]> ExecuteAnalysis(string projectOrSolutionPath, AnalyzerSettings settings,
        PackageInfoCollection packages)
    {
        List<PolicyViolation> violations = new();

        DetectPackageManager(projectOrSolutionPath, settings);
        ChainablePath packageJsonPath = projectOrSolutionPath.ToPath();

        packageJsonPath = packageJsonPath.ResolveFile("package.json");
        if (!packageJsonPath.IsNull)
        {
            var loader = new LockFileLoader(logger);
            ChainablePath lockFile = loader.GetPackageLockFile(packageJsonPath, settings.NpmPackageManager!.Value, settings);

            if (settings.NpmPackageManager == NpmPackageManager.Npm)
            {
                var parser = new NpmLockFileParser(logger);
                await parser.CollectPackageMetadata(lockFile, projectOrSolutionPath, packages);
            }
            else if (settings.NpmPackageManager == NpmPackageManager.Yarn)
            {
                var yarnLoader = new YarnLockFileParser(logger);
                await yarnLoader.CollectPackageMetadata(lockFile.ToString(), projectOrSolutionPath, packages);
            }
            else if (settings.NpmPackageManager == NpmPackageManager.Pnpm)
            {
                var pnpmLoader = new PnpmLockFileParser(logger);
                await pnpmLoader.CollectPackageMetadata(lockFile.ToString(), projectOrSolutionPath, packages);
            }

            ProjectPolicy policy = policyByProject(projectOrSolutionPath);
            violations.AddRange(VerifyAgainstPolicy(packages, policy));
        }

        return violations.ToArray();
    }

    private void DetectPackageManager(string projectOrSolutionPath, AnalyzerSettings settings)
    {
        IDetectPackageManager[] detectors =
        {
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

        logger.LogInformation("Using {PackageManager} as the NPM package manager", settings.NpmPackageManager);
    }

    private PolicyViolation[] VerifyAgainstPolicy(PackageInfoCollection packages, ProjectPolicy policy)
    {
        var violations = new List<PolicyViolation>();

        foreach (PackageInfo package in packages)
        {
            if (!policy.AllowList.Allows(package) || policy.DenyList.Denies(package))
            {
                violations.Add(new PolicyViolation(package.Name, package.Version, package.License!, package.Projects.ToArray(),
                    package.Source, package.SourceUrl));
            }
        }

        return violations.ToArray();
    }
}
