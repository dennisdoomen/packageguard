using System.Text.Json;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.Common;
using NuGet.Versioning;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Fetches license, license URL, and repository URL information from the NPM registry.
/// </summary>
public class NpmRegistryMetadataFetcher(ILogger logger)
{
    private static readonly HttpClient HttpClient = new();

    /// <summary>
    /// One or more NuGet or NPM feeds that should be completely ignored during the analysis.
    /// </summary>
    /// <value>
    /// Each feed is wildcard string that can match the NPM or NuGet feed name or URL.
    /// </value>
    public string[] IgnoredFeeds { get; set; } = [];

    public async Task FetchMetadataAsync(PackageInfo package)
    {
        // Only process packages from npm source
        if (package.Source != "npm")
        {
            return;
        }

        try
        {
            // Extract the registry URL from the package's SourceUrl (resolved field)
            // This supports both public npmjs.org and private npm registries
            string registryUrl = GetRegistryUrl(package);

            // Skip this feed if it's in the ignored list
            if (registryUrl.MatchesAnyWildcard(IgnoredFeeds))
            {
                logger.LogDebug("Ignoring feed {Url}", registryUrl);
                return;
            }

            logger.LogDebug("Fetching NPM package metadata from {Url}", registryUrl);

            string jsonContent = await HttpClient.GetStringAsync(registryUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("time", out JsonElement timeElement) &&
                timeElement.ValueKind == JsonValueKind.Object &&
                timeElement.TryGetProperty(package.Version, out JsonElement publishedElement) &&
                DateTimeOffset.TryParse(publishedElement.GetString(), out DateTimeOffset publishedAt))
            {
                package.PublishedAt = publishedAt;
            }

            if (root.TryGetProperty("dist-tags", out JsonElement distTagsElement) &&
                distTagsElement.ValueKind == JsonValueKind.Object &&
                distTagsElement.TryGetProperty("latest", out JsonElement latestElement))
            {
                string? latestStableVersion = latestElement.GetString();
                if (!string.IsNullOrWhiteSpace(latestStableVersion))
                {
                    package.LatestStableVersion = latestStableVersion;

                    if (TryParseSemanticVersion(latestStableVersion, out NuGetVersion? latestVersion) &&
                        TryParseSemanticVersion(package.Version, out NuGetVersion? currentVersion))
                    {
                        package.IsMajorVersionBehindLatest = latestVersion is not null &&
                                                             currentVersion is not null &&
                                                             latestVersion.Major > currentVersion.Major;
                        package.IsMinorVersionBehindLatest = latestVersion is not null &&
                                                             currentVersion is not null &&
                                                             latestVersion.Major == currentVersion.Major &&
                                                             latestVersion > currentVersion;
                    }
                }
            }

            // Extract license if not already present
            if (package.License is null && root.TryGetProperty("license", out JsonElement licenseElement))
            {
                package.License = licenseElement.GetString();
                logger.LogDebug("Found license for {Name}: {License}", package.Name, package.License);
            }

            // Extract repository URL if not already present
            if (package.RepositoryUrl is null && root.TryGetProperty("repository", out JsonElement repositoryElement))
            {
                if (repositoryElement.ValueKind == JsonValueKind.String)
                {
                    package.RepositoryUrl = repositoryElement.GetString();
                }
                else if (repositoryElement.ValueKind == JsonValueKind.Object &&
                         repositoryElement.TryGetProperty("url", out JsonElement urlElement))
                {
                    string? repoUrl = urlElement.GetString();
                    if (repoUrl is not null)
                    {
                        // Clean up git+ prefix and .git suffix if present
                        package.RepositoryUrl = repoUrl
                            .Replace("git+", "")
                            .Replace("git://", "https://")
                            .TrimEnd('/', '.', 'g', 'i', 't');
                    }
                }

                logger.LogDebug("Found repository URL for {Name}: {Url}", package.Name, package.RepositoryUrl);
            }

            // Extract license URL if available (some packages have this)
            if (package.LicenseUrl is null)
            {
                // Try to construct a license URL from the repository
                if (package.RepositoryUrl is not null && package.RepositoryUrl.Contains("github.com"))
                {
                    // Construct a typical GitHub license URL
                    string cleanUrl = package.RepositoryUrl.TrimEnd('/');
                    package.LicenseUrl = $"{cleanUrl}/blob/master/LICENSE";
                    logger.LogDebug("Constructed license URL for {Name}: {Url}", package.Name, package.LicenseUrl);
                }
            }

            await FetchDownloadCountAsync(package);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Failed to fetch NPM package metadata for {Name} {Version}: {Error}",
                package.Name, package.Version, ex.Message);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Failed to parse NPM package metadata for {Name} {Version}: {Error}",
                package.Name, package.Version, ex.Message);
        }
    }

    /// <summary>
    /// Extracts the registry base URL from the package's SourceUrl (resolved field from package-lock.json).
    /// This supports both public npm registry and private registries.
    /// </summary>
    private string GetRegistryUrl(PackageInfo package)
    {
        string sourceUrl = package.SourceUrl;

        // If SourceUrl is the default fallback, use public registry
        if (sourceUrl == "https://registry.npmjs.org")
        {
            return $"{sourceUrl}/{package.Name}/{package.Version}";
        }

        // Parse the resolved URL to extract the registry base URL
        // Format is typically: https://registry.example.com/package-name/-/package-name-version.tgz
        // We need to construct: https://registry.example.com/package-name/version

        try
        {
            Uri uri = new(sourceUrl);
            string registryBase = $"{uri.Scheme}://{uri.Host}";

            // Add port if not default
            if (uri.Port != 80 && uri.Port != 443 && uri.Port != -1)
            {
                registryBase += $":{uri.Port}";
            }

            // Add path prefix if present (for registries hosted under a subpath)
            string[] pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Find where the package name starts in the path
            // Common patterns:
            // - /package-name/-/package-name-version.tgz (public npm)
            // - /path/to/registry/package-name/-/package-name-version.tgz (private with path)
            // - /@scope/package-name/-/package-name-version.tgz (scoped package)

            int packageIndex = -1;
            string packageNameInUrl = package.Name.Replace("/", "%2f"); // Scoped packages may be URL-encoded
 for (int i = 0; i < pathSegments.Length; i++)
            {
                string decodedSegment = Uri.UnescapeDataString(pathSegments[i]);
                if (decodedSegment == package.Name || decodedSegment == packageNameInUrl)
                {
                    packageIndex = i;
                    break;
                }

                // For scoped packages, check if this segment is the scope
                if (package.Name.StartsWith("@") && decodedSegment.StartsWith("@"))
                {
                    string scope = package.Name.Split('/')[0];
                    if (decodedSegment == scope && i + 1 < pathSegments.Length)
                    {
                        string packagePart = package.Name.Split('/')[1];
                        string nextSegment = Uri.UnescapeDataString(pathSegments[i + 1]);
                        if (nextSegment == packagePart)
                        {
                            packageIndex = i;
                            break;
                        }
                    }
                }
            }

            // Add any path prefix before the package name
            if (packageIndex > 0)
            {
                for (int i = 0; i < packageIndex; i++)
                {
                    registryBase += "/" + pathSegments[i];
                }
            }

            // Construct the final metadata URL
            return $"{registryBase}/{package.Name}/{package.Version}";
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to parse registry URL from {SourceUrl}, falling back to public npm registry: {Error}",
                sourceUrl, ex.Message);

            // Fallback to public npm registry
            return $"https://registry.npmjs.org/{package.Name}/{package.Version}";
        }
    }

    private async Task FetchDownloadCountAsync(PackageInfo package)
    {
        try
        {
            string packageName = Uri.EscapeDataString(package.Name);
            string downloadsUrl = $"https://api.npmjs.org/downloads/point/last-month/{packageName}";
            string jsonContent = await HttpClient.GetStringAsync(downloadsUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonContent);

            if (doc.RootElement.TryGetProperty("downloads", out JsonElement downloadsElement) &&
                downloadsElement.TryGetInt64(out long downloadCount))
            {
                package.DownloadCount = downloadCount;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug("Failed to fetch download count for {Name} {Version}: {Error}",
                package.Name, package.Version, ex.Message);
        }
    }

    private static bool TryParseSemanticVersion(string value, out NuGetVersion? version)
    {
        string normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        return NuGetVersion.TryParse(normalized, out version);
    }
}
