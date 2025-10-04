using System.Text.Json;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.FetchingStrategies;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Fetches license, license URL, and repository URL information from the NPM registry.
/// </summary>
public class NpmRegistryLicenseFetcher(ILogger logger) : IFetchLicense
{
    private static readonly HttpClient HttpClient = new();

    public async Task FetchLicenseAsync(PackageInfo package)
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
            
            logger.LogDebug("Fetching NPM package metadata from {Url}", registryUrl);

            string jsonContent = await HttpClient.GetStringAsync(registryUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

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
            Uri uri = new Uri(sourceUrl);
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
}
