using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace PackageGuard.Core;

public class NuGetPackageAnalyzer(ILogger logger)
{
    private readonly Dictionary<string, SourceRepository[]> nuGetSourcesByProject = new();
    private static readonly HttpClient HttpClient = new();

    public async Task CollectPackageMetadata(string projectPath, string packageName, NuGetVersion packageVersion, PackageInfoCollection packages)
    {
        SourceRepository[] repositories = GetNuGetSources(projectPath);

        PackageInfo? package = packages.Find(packageName, packageVersion.ToNormalizedString());
        if (package is null)
        {
            package = await RetrievePackageMetadata(repositories, packageName, packageVersion);
            if (package is not null)
            {
                packages.Add(package);
                if (package.License is null)
                {
                    await AmendWithLicenseInformation(package);
                }
            }
            else
            {
                logger.LogWarning("Package {Name} {Version} not found in any of the sources", packageName, packageVersion);
            }
        }
        else
        {
            logger.LogDebug("Already scanned {Name} {Version}", packageName, packageVersion);
        }

        package?.Add(projectPath);
    }

    private SourceRepository[] GetNuGetSources(string projectDirectory)
    {
        if (!nuGetSourcesByProject.TryGetValue(projectDirectory, out SourceRepository[]? sources))
        {
            logger.LogInformation("Finding NuGet sources");

            var settings = NuGet.Configuration.Settings.LoadDefaultSettings(projectDirectory);
            var sourceProvider = new PackageSourceProvider(settings);
            var packageSources = sourceProvider.LoadPackageSources()
                .Where(s => s.IsEnabled)
                .ToList();

            foreach (PackageSource source in packageSources)
            {
                logger.LogDebug("Found NuGet source {Name} ({Source})", source.Name, source.Source);
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

    private async Task<PackageInfo?> RetrievePackageMetadata(SourceRepository[] repositories, string packageName, NuGetVersion packageVersion)
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
                    Source = repository.PackageSource.Source
                };
            }
        }

        return null;
    }

    private async Task AmendWithLicenseInformation(PackageInfo package)
    {
        logger.LogInformation("Package {Name} {Version} does not have a license. Fetching it separately", package.Id, package.Version);
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PackageGuard", "v1"));

        if (package.RepositoryUrl is not null)
        {
            const string validCharacters = "[a-zA-Z0-9._-]";

            var match = Regex.Match(package.RepositoryUrl,
                $@"raw.githubusercontent.com\/(?<owner>{validCharacters}+?)\/(?<repo>{validCharacters}+)");

            if (match.Length == 0)
            {
                match = Regex.Match(package.RepositoryUrl,
                    $@"github.com\/(?<owner>{validCharacters}+?)\/(?<repo>{validCharacters}+)");
            }

            if (match.Length > 0)
            {
                string owner = match.Groups["owner"].Value;
                string repo = match.Groups["repo"].Value;

                string url = $"https://api.github.com/repos/{owner}/{repo}/license";

                string licenseJson = await HttpClient.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(licenseJson);
                package.License = doc.RootElement.GetProperty("license").GetProperty("spdx_id").GetString();
            }
        }

        if (package.License is null)
        {
            package.License = "Unknown";

            try
            {
                string licenseText = await HttpClient.GetStringAsync(package.LicenseUrl);

                if (licenseText.Contains("MIT license", StringComparison.OrdinalIgnoreCase))
                {
                    package.License = "MIT";
                }
                else if (licenseText.Contains("Apache License", StringComparison.OrdinalIgnoreCase))
                {
                    package.License = "Apache-2.0";
                }
                else if (licenseText.Contains("GNU General Public License", StringComparison.OrdinalIgnoreCase))
                {
                    package.License = "GPL-3.0";
                }
                else
                {
                    logger.LogWarning("Did not detect any well-known licenses in {URL}", package.LicenseUrl);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Failed to extract the license from URL {URL}: {ErrorCode}", package.LicenseUrl, ex.Message);
            }
        }

        logger.LogInformation("Determined the license to be {License}", package.License);
    }
}
