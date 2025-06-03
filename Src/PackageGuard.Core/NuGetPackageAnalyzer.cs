using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageGuard.Core.Common;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace PackageGuard.Core;

/// <summary>
/// Represents an analyzer for NuGet packages within a specified project.
/// Used to collect metadata for packages, retrieve package information from
/// configured repositories, and augment packages with license information as needed.
/// </summary>
public class NuGetPackageAnalyzer(ILogger logger, LicenseFetcher licenseFetcher)
{
    private readonly Dictionary<string, SourceRepository[]> nuGetSourcesByProject = new();

    /// <summary>
    /// One or more NuGet feeds that should be completely ignored during the analysis.
    /// </summary>
    /// <value>
    /// Each feed is wildcard string that can match the NuGet feed name or URL.
    /// </value>
    public string[] IgnoredFeeds { get; set; } = [];

    public async Task CollectPackageMetadata(string projectPath, string packageName, NuGetVersion packageVersion,
        PackageInfoCollection packages)
    {
        SourceRepository[] repositories = GetNuGetSources(projectPath);

        PackageInfo? package = packages.Find(packageName, packageVersion.ToNormalizedString());
        if (package is not null)
        {
            logger.LogDebug("Already scanned {Name} {Version}", packageName, packageVersion);
        }
        else
        {
            package = await RetrievePackageMetadata(repositories, packageName, packageVersion);
            if (package is not null)
            {
                packages.Add(package);
                await licenseFetcher.AmendWithMissingLicenseInformation(package);
            }
            else
            {
                logger.LogWarning("Package {Name} {Version} not found in any of the sources", packageName, packageVersion);
            }
        }

        package?.TrackAsUsedInProject(projectPath);
    }

    private SourceRepository[] GetNuGetSources(string projectDirectory)
    {
        if (!nuGetSourcesByProject.TryGetValue(projectDirectory, out SourceRepository[]? sources))
        {
            logger.LogInformation("Finding NuGet sources");

            var settings = Settings.LoadDefaultSettings(projectDirectory);
            var sourceProvider = new PackageSourceProvider(settings);

            var packageSources = new List<PackageSource>();
            foreach (PackageSource source in sourceProvider.LoadPackageSources().Where(s => s.IsEnabled))
            {
                if (IgnoredFeeds.Any(pattern => source.Name.MatchesWildcard(pattern) ||
                                                source.Source.MatchesWildcard(pattern)))
                {
                    logger.LogDebug("Ignoring NuGet source {Name} ({Source})", source.Name, source.Source);
                }
                else
                {
                    logger.LogDebug("Found NuGet source {Name} ({Source})", source.Name, source.Source);
                    packageSources.Add(source);
                }
            }

            if (!packageSources.Any())
            {
                throw new ApplicationException("No NuGet sources found in configuration.");
            }

            var providers = Repository.Provider.GetCoreV3();
            sources = packageSources.Select(s => new SourceRepository(s, providers)).ToArray();

            nuGetSourcesByProject[projectDirectory] = sources;
        }

        return nuGetSourcesByProject[projectDirectory];
    }

    private async Task<PackageInfo?> RetrievePackageMetadata(SourceRepository[] repositories, string packageName,
        NuGetVersion packageVersion)
    {
        logger.LogInformation("Retrieving metadata for {Name} {Version}", packageName, packageVersion);

        foreach (SourceRepository repository in repositories)
        {
            var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>();

            var packageMetadata = await metadataResource.GetMetadataAsync(
                packageName,
                includePrerelease: true,
                includeUnlisted: true,
                sourceCacheContext: new SourceCacheContext(), NullLogger.Instance,
                token: CancellationToken.None);

            IPackageSearchMetadata? packageInfo =
                packageMetadata?.FirstOrDefault(p =>
                    p.Identity.Version.ToNormalizedString() == packageVersion.ToNormalizedString());

            if (packageInfo != null)
            {
                return new PackageInfo
                {
                    Id = packageInfo.Identity.Id,
                    Version = packageInfo.Identity.Version.ToNormalizedString(),
                    RepositoryUrl = packageInfo.ProjectUrl?.ToString(),
                    License = packageInfo.LicenseMetadata?.License,
                    LicenseUrl = packageInfo.LicenseUrl?.ToString(),
                    Source = repository.PackageSource.Name,
                    SourceUrl = repository.PackageSource.Source
                };
            }
        }

        return null;
    }
}
