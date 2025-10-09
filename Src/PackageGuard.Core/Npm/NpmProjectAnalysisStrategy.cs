using Microsoft.Extensions.Logging;
using Pathy;
using static PackageGuard.Core.NpmPackageManager;

namespace PackageGuard.Core.Npm;

public class NpmProjectAnalysisStrategy(GetPolicyByProject policyByProject, ILogger logger) : IProjectAnalysisStrategy
{
    public async Task<PolicyViolation[]> ExecuteAnalysis(string projectOrSolutionPath, AnalyzerSettings settings,
        PackageInfoCollection packages)
    {
        List<PolicyViolation> violations = new();

        // Based on the settings, files on disk or the environment, determine which package manager to use
        DetectPackageManager(projectOrSolutionPath, settings);

        // Find the package.json file, either because the path points to it directly, or it's in the folder
        ChainablePath packageJsonPath = projectOrSolutionPath.ToPath();
        packageJsonPath = packageJsonPath.ResolveFile("package.json");

        if (!packageJsonPath.IsNull)
        {
            // Load the existing lock file, or if it doesn't exist, force an install of all dependencies
            var loader = new LockFileLoader(logger);
            ChainablePath lockFile = loader.GetPackageLockFile(packageJsonPath, settings.NpmPackageManager!.Value, settings);

            var metadataFetcher = new NpmRegistryMetadataFetcher(logger);
            await CollectPackageMetadataFromLockFile(lockFile, settings, packages, metadataFetcher);

            ProjectPolicy policy = policyByProject(lockFile.Directory);
            violations.AddRange(VerifyAgainstPolicy(packages, policy));
        }

        return violations.ToArray();
    }

    private async Task CollectPackageMetadataFromLockFile(ChainablePath lockFile, AnalyzerSettings settings,
        PackageInfoCollection packages, NpmRegistryMetadataFetcher metadataFetcher)
    {
        if (settings.NpmPackageManager == NpmPackageManager.Npm)
        {
            var parser = new NpmLockFileParser(metadataFetcher, logger);
            await parser.CollectPackageMetadata(lockFile, packages);
        }
        else if (settings.NpmPackageManager == Yarn)
        {
            var parser = new YarnLockFileParser(logger);
            await parser.CollectPackageMetadata(lockFile.ToString(), packages);
        }
        else if (settings.NpmPackageManager == Pnpm)
        {
            var parser = new PnpmLockFileParser(logger);
            await parser.CollectPackageMetadata(lockFile.ToString(), packages);
        }
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

        if (settings.NpmPackageManager is not null or None)
        {
            logger.LogInformation("Using {PackageManager} as the NPM package manager", settings.NpmPackageManager);
        }
        else
        {
            logger.LogInformation("No NPM package manager detected or specified, so skipping NPM analysis");
        }
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
