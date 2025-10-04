using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Loads and parses Yarn yarn.lock files to extract package information.
/// </summary>
public class YarnLockFileLoader
{
    private readonly NpmRegistryLicenseFetcher licenseFetcher;

    public ILogger Logger { get; set; } = NullLogger.Instance;

    public YarnLockFileLoader(ILogger? logger = null)
    {
        Logger = logger ?? NullLogger.Instance;
        licenseFetcher = new NpmRegistryLicenseFetcher(Logger);
    }

    /// <summary>
    /// Parses a Yarn yarn.lock file and populates a <see cref="PackageInfoCollection"/> with the packages found.
    /// </summary>
    /// <param name="yarnLockPath">The full path to the yarn.lock file.</param>
    /// <param name="projectPath">The path to the project that contains the yarn.lock file.</param>
    /// <param name="packages">The collection to populate with package information.</param>
    public async Task CollectPackageMetadata(string yarnLockPath, string projectPath, PackageInfoCollection packages)
    {
        if (!File.Exists(yarnLockPath))
        {
            Logger.LogWarning("yarn.lock file not found at {Path}", yarnLockPath);
            return;
        }

        try
        {
            Logger.LogInformation("Loading Yarn lock file from {Path}", yarnLockPath);

            string content = File.ReadAllText(yarnLockPath);
            var parsedPackages = ParseYarnLock(content);

            Logger.LogInformation("Successfully parsed yarn.lock with {PackageCount} packages", parsedPackages.Count);

            foreach (var (packageName, packageData) in parsedPackages)
            {
                var packageInfo = new PackageInfo
                {
                    Name = packageName,
                    Version = packageData.Version,
                    License = null, // Yarn lock files typically don't include license info
                    Source = "npm",
                    SourceUrl = packageData.Resolved ?? "https://registry.npmjs.org"
                };

                packages.Add(packageInfo);
                packageInfo.TrackAsUsedInProject(projectPath);

                // Fetch additional metadata from NPM registry since Yarn lock doesn't include license
                await licenseFetcher.FetchLicenseAsync(packageInfo);

                Logger.LogDebug("Added Yarn package {Name} {Version} with license {License}",
                    packageName, packageData.Version, packageInfo.License ?? "Unknown");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse yarn.lock from {Path}", yarnLockPath);
        }
    }

    /// <summary>
    /// Parses the yarn.lock file format into a dictionary of packages.
    /// </summary>
    private Dictionary<string, YarnPackageData> ParseYarnLock(string content)
    {
        var packages = new Dictionary<string, YarnPackageData>();
        var lines = content.Split('\n');
        
        string? currentPackageName = null;
        YarnPackageData? currentPackageData = null;
        int currentIndent = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            
            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
            {
                continue;
            }

            // Calculate indentation level
            int indent = line.Length - line.TrimStart().Length;

            // Package declaration (starts with quote or at indent 0)
            if (indent == 0 && (line.StartsWith("\"") || line.StartsWith("'") || !line.StartsWith(" ")))
            {
                // Save previous package if exists
                if (currentPackageName != null && currentPackageData != null)
                {
                    packages[currentPackageName] = currentPackageData;
                }

                // Parse package name from declaration like: "@babel/core@^7.0.0", @babel/core@^7.0.0:
                string packageDeclaration = line.TrimEnd(':');
                // Remove quotes if present
                packageDeclaration = packageDeclaration.Trim('"', '\'');
                
                // Extract actual package name (before @ version specifier)
                // Handle scoped packages like @babel/core@^7.0.0
                string packageName;
                if (packageDeclaration.StartsWith("@"))
                {
                    // Scoped package: @scope/package@version
                    var match = Regex.Match(packageDeclaration, @"^(@[^/]+/[^@]+)@");
                    packageName = match.Success ? match.Groups[1].Value : packageDeclaration.Split('@')[0] + "/" + packageDeclaration.Split('@')[1];
                }
                else
                {
                    // Regular package: package@version
                    packageName = packageDeclaration.Split('@')[0];
                }

                currentPackageName = packageName;
                currentPackageData = new YarnPackageData();
                currentIndent = indent;
            }
            else if (currentPackageData != null && indent > currentIndent)
            {
                // Property of current package
                string trimmedLine = line.Trim();
                
                if (trimmedLine.StartsWith("version "))
                {
                    currentPackageData.Version = ExtractValue(trimmedLine, "version");
                }
                else if (trimmedLine.StartsWith("resolved "))
                {
                    currentPackageData.Resolved = ExtractValue(trimmedLine, "resolved");
                }
            }
        }

        // Save last package
        if (currentPackageName != null && currentPackageData != null)
        {
            packages[currentPackageName] = currentPackageData;
        }

        return packages;
    }

    private string ExtractValue(string line, string prefix)
    {
        string value = line.Substring(prefix.Length).Trim();
        // Remove quotes
        value = value.Trim('"', '\'');
        return value;
    }

    private class YarnPackageData
    {
        public string Version { get; set; } = "";
        public string? Resolved { get; set; }
    }
}
