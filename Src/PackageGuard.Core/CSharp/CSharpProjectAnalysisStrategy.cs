using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.ProjectModel;
using PackageGuard.Core.Common;

namespace PackageGuard.Core.CSharp;

/// <summary>
/// Analyzes C# projects for compliance with defined policies, such as allowed and denied packages, licenses, and feeds.
/// </summary>
public class CSharpProjectAnalysisStrategy(GetPolicyByProject getPolicyByProject, LicenseFetcher licenseFetcher, ILogger? logger)
    : IProjectAnalysisStrategy
{
    private readonly ILogger logger = logger ?? NullLogger.Instance;

    public async Task<PolicyViolation[]> ExecuteAnalysis(string projectOrSolutionPath, AnalyzerSettings settings,
        PackageInfoCollection packages)
    {
        List<PolicyViolation> violations = new();

        if (settings.ScanNuGet)
        {
            var analyzer = new NuGetPackageAnalyzer(logger, licenseFetcher);
            var scanner = new CSharpProjectScanner(logger);

            analyzer.InteractiveRestore = settings.InteractiveRestore;

            List<string> projectPaths = scanner.FindProjects(projectOrSolutionPath);
            foreach (string projectPath in projectPaths)
            {
                packages.Clear();

                ProjectPolicy policy = getPolicyByProject(projectPath);
                policy.Validate();

                analyzer.IgnoredFeeds = policy.IgnoredFeeds;

                await CollectPackagesFrom(projectPath, settings, packages, analyzer);

                violations.AddRange(VerifyAgainstPolicy(packages, policy));
            }
        }
        else
        {
            logger.LogInformation("NuGet scanning was disabled, so skipping .NET package analysis");
        }

        return violations.ToArray();
    }

    private async Task CollectPackagesFrom(string projectPath, AnalyzerSettings settings, PackageInfoCollection packages,
        NuGetPackageAnalyzer analyzer)
    {
        logger.LogHeader($"Getting metadata for packages in {projectPath}");

        var lockFileLoader = new DotNetLockFileLoader
        {
            Logger = logger,
            InteractiveRestore = settings.InteractiveRestore,
            ForceRestore = settings.ForceRestore,
            SkipRestore = settings.SkipRestore,
        };

        LockFile? lockFile = lockFileLoader.GetPackageLockFile(projectPath);
        if (lockFile is not null)
        {
            foreach (LockFileLibrary? library in lockFile.Libraries.Where(library => library.Type == "package"))
            {
                await analyzer.CollectPackageMetadata(projectPath, library.Name, library.Version, packages);
            }
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
