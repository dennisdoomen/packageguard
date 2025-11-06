using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Npm;

namespace PackageGuard.Core;

/// <summary>
/// Analyzes C# projects for compliance with defined policies, such as allowed and denied packages, licenses, and feeds.
/// </summary>
public class ProjectAnalyzer(LicenseFetcher licenseFetcher)
{
    public ILogger Logger { get; set; } = NullLogger.Instance;

    public async Task<PolicyViolation[]> ExecuteAnalysis(string projectPath, AnalyzerSettings settings, GetPolicyByProject getPolicyByProject)
    {
        IProjectAnalysisStrategy[] strategies =
        [
            new CSharpProjectAnalysisStrategy(getPolicyByProject, licenseFetcher, Logger),
            new NpmProjectAnalysisStrategy(getPolicyByProject, Logger)
        ];

        List<PolicyViolation> violations = new();

        PackageInfoCollection packages = new(Logger);
        if (settings.UseCaching && settings.CacheFilePath.Length > 0)
        {
            Logger.LogInformation("Try loading package cache from {CacheFilePath}", settings.CacheFilePath);
            await packages.TryInitializeFromCache(settings.CacheFilePath);
        }

        foreach (IProjectAnalysisStrategy strategy in strategies)
        {
            violations.AddRange(await strategy.ExecuteAnalysis(projectPath, settings, packages));
        }

        if (settings.UseCaching)
        {
            await packages.WriteToCache(settings.CacheFilePath);
        }

        return violations.ToArray();
    }
}
