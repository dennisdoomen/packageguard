using Microsoft.Extensions.Logging;
using PackageGuard.Core.Common;
using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;
using Pathy;
using static PackageGuard.Core.Npm.NpmPackageManager;

namespace PackageGuard.Core.Npm;

public class NpmProjectAnalysisStrategy(GetPolicyByProject policyByProject, ILogger logger) : IProjectAnalysisStrategy
{
    public async Task<PolicyViolation[]> ExecuteAnalysis(string projectOrSolutionPath, AnalyzerSettings settings,
        PackageInfoCollection packages)
    {
        List<PolicyViolation> violations = new();

        string target = string.IsNullOrWhiteSpace(settings.NpmProjectPath)
            ? GetDirectoryIfDotNetFile(projectOrSolutionPath)
            : settings.NpmProjectPath;

        // Based on the settings, files on disk or the environment, determine which package manager to use. When the
        // target is a lock file, its name decides, even if the directory also contains files of another package manager.
        bool isPackageJson = Path.GetFileName(target).Equals("package.json", StringComparison.OrdinalIgnoreCase);
        DetectPackageManager(isPackageJson ? GetDirectoryIfNpmFile(target) : target, settings);

        projectOrSolutionPath = GetDirectoryIfNpmFile(target);

        if (settings.NpmPackageManager == NpmPackageManager.None)
        {
            logger.LogInformation("NPM scanning is explicitly disabled, so skipping NPM analysis");

            return violations.ToArray();
        }

        // Find the package.json file, either because the path points to it directly, or it's in the folder
        ChainablePath packageJsonPath = projectOrSolutionPath.ToPath();
        packageJsonPath = packageJsonPath.ResolveFile("package.json");

        if (!packageJsonPath.IsNull)
        {
            // Load the existing lock file, or if it doesn't exist, force an install of all dependencies
            var loader = new LockFileLoader(logger);
            ChainablePath lockFile = loader.GetPackageLockFile(packageJsonPath, settings.NpmPackageManager!.Value, settings);

            ProjectPolicy policy = policyByProject(lockFile.Directory);

            await CollectPackageMetadataFrom(lockFile, settings, packages, new NpmRegistryMetadataFetcher(logger)
            {
                IgnoredFeeds = policy.IgnoredFeeds
            });

            violations.AddRange(VerifyAgainstPolicy(packages, policy));
        }
        else
        {
            logger.LogWarning("No package.json file found in {ProjectOrSolutionPath}", projectOrSolutionPath);
        }

        return violations.ToArray();
    }

    /// <summary>
    /// When the path points to a .NET solution or project file, the npm files live next to it, so look in its directory.
    /// </summary>
    private static string GetDirectoryIfDotNetFile(string path)
    {
        string extension = Path.GetExtension(path);

        bool isDotNetFile = extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase);

        return isDotNetFile ? Path.GetDirectoryName(Path.GetFullPath(path))! : path;
    }

    /// <summary>
    /// When the explicit npm path points to a package.json or lock file, use its directory so the package manager is detected from the files next to it.
    /// </summary>
    private static string GetDirectoryIfNpmFile(string path)
    {
        string[] lockFileNames = ["package.json", "package-lock.json", "yarn.lock", "pnpm-lock.yaml"];

        return lockFileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.GetFullPath(path))!
            : path;
    }

    private async Task CollectPackageMetadataFrom(ChainablePath lockFile, AnalyzerSettings settings,
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

        await metadataFetcher.ResolveDownloadCountsAsync();
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

        if (settings.NpmPackageManager is not null || settings.NpmPackageManager == NpmPackageManager.Npm)
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
            if (package.SourceUrl.MatchesAnyWildcard(policy.IgnoredFeeds))
            {
                continue;
            }

            PolicyDecision allowDecision = policy.AllowList.EvaluateAllow(package);
            PolicyDecision denyDecision = policy.DenyList.EvaluateDeny(package);

            if (!allowDecision.IsMatch || denyDecision.IsMatch)
            {
                PolicyDecision decision = denyDecision.IsMatch ? denyDecision : allowDecision;
                violations.Add(new PolicyViolation(package.Name, package.Version, package.License!, package.Projects.ToArray(),
                    package.Source, package.SourceUrl, decision.Reason));
            }
            else
            {
                PolicyDecision warnDecision = policy.WarnList.EvaluateWarn(package);
                if (warnDecision.IsMatch)
                {
                    violations.Add(new PolicyViolation(package.Name, package.Version, package.License!, package.Projects.ToArray(),
                        package.Source, package.SourceUrl, warnDecision.Reason, IsWarning: true));
                }
            }
        }

        return violations.ToArray();
    }
}
