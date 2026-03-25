using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageGuard.Core.Common;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace PackageGuard.Core.CSharp;

/// <summary>
/// Represents an analyzer for NuGet packages within a specified project.
/// Used to collect metadata for packages, retrieve package information from
/// configured repositories, and augment packages with license information as needed.
/// </summary>
public class NuGetPackageAnalyzer(ILogger logger, LicenseFetcher licenseFetcher)
{
    private readonly Dictionary<string, SourceRepository[]> nuGetSourcesByProject = new();
    private static bool credentialProvidersConfigured;
    private static readonly Lock CredentialProviderLock = new();

    /// <summary>
    /// One or more NuGet feeds that should be completely ignored during the analysis.
    /// </summary>
    /// <value>
    /// Each feed is wildcard string that can match the NuGet feed name or URL.
    /// </value>
    public string[] IgnoredFeeds { get; set; } = [];

    /// <summary>
    /// Specifies whether interactive mode should be enabled for the .NET restore process.
    /// When enabled, the restore operation may prompt for user input, such as authentication information.
    /// </summary>
    public bool InteractiveRestore { get; set; } = true;

    public async Task CollectPackageMetadata(string projectPath, string packageName, NuGetVersion packageVersion,
        PackageInfoCollection packages)
    {
        SourceRepository[] projectNuGetSources = GetNuGetSources(projectPath);

        PackageInfo? package = packages.Find(packageName, packageVersion.ToNormalizedString(), projectNuGetSources);
        if (package is not null)
        {
            logger.LogDebug("Already scanned {Name} {Version}", packageName, packageVersion);
        }
        else
        {
            package = await RetrievePackageMetadata(projectNuGetSources, packageName, packageVersion);
            if (package is not null)
            {
                package = packages.Add(package);
                if (package.License is null)
                {
                    await licenseFetcher.AmendWithMissingLicenseInformation(package);
                }
            }
            else
            {
                logger.LogWarning("Package {Name} {Version} not found in any of the sources", packageName, packageVersion);
            }
        }

        if (package is not null)
        {
            UpdateRepositoryUrlFromLocalPackage(projectPath, package, packageVersion);
        }

        package?.TrackAsUsedInProject(projectPath);
    }

    private SourceRepository[] GetNuGetSources(string projectDirectory)
    {
        if (!nuGetSourcesByProject.TryGetValue(projectDirectory, out SourceRepository[]? sources))
        {
            logger.LogInformation("Finding NuGet sources");

            // Ensure credential providers are configured for Azure DevOps and other authenticated feeds
            EnsureCredentialProvidersConfigured();

            var settings = Settings.LoadDefaultSettings(projectDirectory);
            var sourceProvider = new PackageSourceProvider(settings);
            PackageSource[] configuredSources = sourceProvider.LoadPackageSources()
                .Where(source => source.IsEnabled)
                .ToArray();

            var packageSources = new List<PackageSource>();
            foreach (PackageSource source in configuredSources)
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

    /// <summary>
    /// Ensures that NuGet credential providers (including Git Credential Manager) are configured.
    /// This is necessary for authenticating against Azure DevOps feeds and other authenticated sources.
    /// </summary>
    private void EnsureCredentialProvidersConfigured()
    {
        lock (CredentialProviderLock)
        {
            if (!credentialProvidersConfigured)
            {
                DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance, nonInteractive: !InteractiveRestore);

                credentialProvidersConfigured = true;
            }
        }
    }

    private async Task<PackageInfo?> RetrievePackageMetadata(SourceRepository[] projectNuGetSources, string packageName,
        NuGetVersion packageVersion)
    {
        logger.LogInformation("Retrieving metadata for {Name} {Version}", packageName, packageVersion);

        foreach (SourceRepository nuGetSource in projectNuGetSources)
        {
            var metadataResource = await nuGetSource.GetResourceAsync<PackageMetadataResource>();

            IPackageSearchMetadata[] packageMetadata = (await metadataResource.GetMetadataAsync(
                packageName,
                includePrerelease: true,
                includeUnlisted: true,
                sourceCacheContext: new SourceCacheContext(), NullLogger.Instance,
                token: CancellationToken.None))?.ToArray() ?? [];

            IPackageSearchMetadata? packageInfo =
                packageMetadata.FirstOrDefault(p =>
                    p.Identity.Version.ToNormalizedString() == packageVersion.ToNormalizedString());

            if (packageInfo != null)
            {
                NuGetVersion? latestStableVersion = packageMetadata
                    .Where(p => !p.Identity.Version.IsPrerelease)
                    .Select(p => p.Identity.Version)
                    .OrderByDescending(version => version)
                    .FirstOrDefault();

                IPackageSearchMetadata? latestStableMetadata = latestStableVersion is null
                    ? null
                    : packageMetadata.FirstOrDefault(p => p.Identity.Version == latestStableVersion);

                double? versionUpdateLagDays = packageInfo.Published != null &&
                                               latestStableMetadata?.Published != null &&
                                               latestStableMetadata.Published.Value > packageInfo.Published.Value
                    ? (latestStableMetadata.Published.Value - packageInfo.Published.Value).TotalDays
                    : null;

                return new PackageInfo
                {
                    Name = packageInfo.Identity.Id,
                    Version = packageInfo.Identity.Version.ToNormalizedString(),
                    RepositoryUrl = packageInfo.ProjectUrl?.ToString(),
                    License = packageInfo.LicenseMetadata?.License,
                    LicenseUrl = packageInfo.LicenseUrl?.ToString(),
                    IsDeprecated = LooksDeprecated(packageInfo),
                    PublishedAt = packageInfo.Published,
                    DownloadCount = packageInfo.DownloadCount,
                    LatestStableVersion = latestStableVersion?.ToNormalizedString(),
                    LatestStablePublishedAt = latestStableMetadata?.Published,
                    VersionUpdateLagDays = versionUpdateLagDays,
                    IsMajorVersionBehindLatest = latestStableVersion is not null &&
                                                 latestStableVersion.Major > packageInfo.Identity.Version.Major,
                    IsMinorVersionBehindLatest = latestStableVersion is not null &&
                                                 latestStableVersion.Major == packageInfo.Identity.Version.Major &&
                                                 latestStableVersion > packageInfo.Identity.Version,
                    Source = nuGetSource.PackageSource.Name,
                    SourceUrl = nuGetSource.PackageSource.Source
                };
            }
        }

        return null;
    }

    private static bool LooksDeprecated(IPackageSearchMetadata packageInfo)
    {
        string text = $"{packageInfo.Tags} {packageInfo.Summary} {packageInfo.Description}";
        return text.Contains("deprecated", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("deprecation", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("obsolete", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateRepositoryUrlFromLocalPackage(string projectPath, PackageInfo package, NuGetVersion packageVersion)
    {
        string? repositoryUrl = TryGetRepositoryUrlFromNuspec(projectPath, package.Name, packageVersion);
        if (!string.IsNullOrWhiteSpace(repositoryUrl))
        {
            package.RepositoryUrl = repositoryUrl;
        }
    }

    private string? TryGetRepositoryUrlFromNuspec(string projectPath, string packageName, NuGetVersion packageVersion)
    {
        string globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(Settings.LoadDefaultSettings(projectPath));
        string packageId = packageName.ToLowerInvariant();

        foreach (string version in EnumerateVersionCandidates(packageVersion))
        {
            string nuspecPath = Path.Combine(globalPackagesFolder, packageId, version, $"{packageId}.nuspec");
            if (!File.Exists(nuspecPath))
            {
                continue;
            }

            try
            {
                return ReadRepositoryUrlFromNuspec(nuspecPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
            {
                logger.LogDebug("Failed to read repository metadata from {NuspecPath}: {Error}", nuspecPath, ex.Message);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateVersionCandidates(NuGetVersion packageVersion)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string normalized = packageVersion.ToNormalizedString().ToLowerInvariant();
        if (seen.Add(normalized))
        {
            yield return normalized;
        }

        string original = packageVersion.ToString().ToLowerInvariant();
        if (seen.Add(original))
        {
            yield return original;
        }
    }

    private static string? ReadRepositoryUrlFromNuspec(string nuspecPath)
    {
        XDocument document = XDocument.Load(nuspecPath);
        string? repositoryUrl = document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "repository")
            ?.Attribute("url")
            ?.Value;

        return string.IsNullOrWhiteSpace(repositoryUrl) ? null : repositoryUrl;
    }
}
