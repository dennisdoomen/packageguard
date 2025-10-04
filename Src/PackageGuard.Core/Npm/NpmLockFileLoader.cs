using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PackageGuard.Core.FetchingStrategies;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Loads and parses npm package-lock.json files to extract package information.
/// </summary>
public class NpmLockFileLoader
{
    private readonly NpmRegistryLicenseFetcher licenseFetcher;

    public ILogger Logger { get; set; } = NullLogger.Instance;

    public NpmLockFileLoader(ILogger? logger = null)
    {
        Logger = logger ?? NullLogger.Instance;
        licenseFetcher = new NpmRegistryLicenseFetcher(Logger);
    }

    /// <summary>
    /// Loads and parses a package-lock.json file from the specified path.
    /// </summary>
    /// <param name="packageLockPath">The full path to the package-lock.json file.</param>
    /// <returns>An <see cref="NpmPackageLock"/> object containing the parsed data, or null if parsing fails.</returns>
    internal NpmPackageLock? LoadPackageLockFile(string packageLockPath)
    {
        if (!File.Exists(packageLockPath))
        {
            Logger.LogWarning("Package-lock.json file not found at {Path}", packageLockPath);
            return null;
        }

        try
        {
            Logger.LogInformation("Loading npm lock file from {Path}", packageLockPath);

            string jsonContent = File.ReadAllText(packageLockPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };

            NpmPackageLock? lockFile = JsonSerializer.Deserialize<NpmPackageLock>(jsonContent, options);

            if (lockFile == null)
            {
                Logger.LogWarning("Failed to deserialize package-lock.json from {Path}", packageLockPath);
                return null;
            }

            Logger.LogInformation("Successfully loaded npm lock file with {PackageCount} packages",
                lockFile.Packages?.Count ?? 0);

            return lockFile;
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse package-lock.json from {Path}", packageLockPath);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error while loading package-lock.json from {Path}", packageLockPath);
            return null;
        }
    }

    /// <summary>
    /// Parses an npm package-lock.json file and populates a <see cref="PackageInfoCollection"/> with the packages found.
    /// </summary>
    /// <param name="packageLockPath">The full path to the package-lock.json file.</param>
    /// <param name="projectPath">The path to the project that contains the package-lock.json file.</param>
    /// <param name="packages">The collection to populate with package information.</param>
    public async Task CollectPackageMetadata(string packageLockPath, string projectPath, PackageInfoCollection packages)
    {
        NpmPackageLock? lockFile = LoadPackageLockFile(packageLockPath);

        if (lockFile?.Packages == null)
        {
            return;
        }

        foreach (var (packagePath, packageEntry) in lockFile.Packages)
        {
            // Skip the root package entry (empty string key)
            if (string.IsNullOrEmpty(packagePath))
            {
                continue;
            }

            // Extract package name from the path (e.g., "node_modules/express" -> "express")
            string packageName = packagePath.StartsWith("node_modules/")
                ? packagePath.Substring("node_modules/".Length)
                : packagePath;

            // Handle scoped packages (e.g., "@types/node")
            if (packageName.Contains("/") && packageName.StartsWith("@"))
            {
                // For scoped packages, keep the full name including scope
                // Already in correct format
            }
            else if (packageName.Contains("/"))
            {
                // For nested dependencies like "node_modules/express/node_modules/accepts",
                // extract the last part
                var parts = packageName.Split('/');
                int nodeModulesIndex = Array.LastIndexOf(parts, "node_modules");
                if (nodeModulesIndex >= 0 && nodeModulesIndex < parts.Length - 1)
                {
                    packageName = parts[nodeModulesIndex + 1];
                }
            }

            if (string.IsNullOrWhiteSpace(packageEntry.Version))
            {
                Logger.LogDebug("Skipping package {Name} with no version", packageName);
                continue;
            }

            var packageInfo = new PackageInfo
            {
                Name = packageName,
                Version = packageEntry.Version,
                License = packageEntry.License,
                Source = "npm",
                SourceUrl = packageEntry.Resolved ?? "https://registry.npmjs.org"
            };

            packages.Add(packageInfo);
            packageInfo.TrackAsUsedInProject(projectPath);

            // Fetch additional metadata from NPM registry if license is missing
            if (packageInfo.License is null)
            {
                await licenseFetcher.FetchLicenseAsync(packageInfo);
            }

            Logger.LogDebug("Added npm package {Name} {Version} with license {License}",
                packageName, packageEntry.Version, packageInfo.License ?? "Unknown");
        }
    }
}
