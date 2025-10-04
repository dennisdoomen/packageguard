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
            // Build the NPM registry URL for the specific package version
            string registryUrl = $"https://registry.npmjs.org/{package.Name}/{package.Version}";
            
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
}
