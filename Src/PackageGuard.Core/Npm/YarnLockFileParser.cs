using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pathy;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Loads and parses Yarn yarn.lock files (both v1 and v2 formats) to extract package information.
/// </summary>
internal class YarnLockFileParser
{
    private readonly NpmRegistryMetadataFetcher metadataFetcher;

    private readonly ILogger logger;

    public YarnLockFileParser(ILogger? logger = null)
    {
        this.logger = logger ?? NullLogger.Instance;
        metadataFetcher = new NpmRegistryMetadataFetcher(this.logger);
    }

    /// <summary>
    /// Parses a Yarn yarn.lock file and populates a <see cref="PackageInfoCollection"/> with the packages found.
    /// Supports both Yarn v1 (custom text format) and Yarn v2+ (YAML format).
    /// </summary>
    /// <param name="yarnLockPath">The full path to the yarn.lock file.</param>
    /// <param name="packages">The collection to populate with package information.</param>
    public async Task CollectPackageMetadata(ChainablePath yarnLockPath, PackageInfoCollection packages)
    {
        if (!File.Exists(yarnLockPath))
        {
            logger.LogWarning("yarn.lock file not found at {Path}", yarnLockPath);
            return;
        }

        try
        {
            logger.LogInformation("Loading Yarn lock file from {Path}", yarnLockPath);

            string content = File.ReadAllText(yarnLockPath);

            // Detect Yarn version based on format
            bool isYarnV2 = IsYarnV2Format(content);

            Dictionary<string, YarnPackageData> parsedPackages;
            if (isYarnV2)
            {
                logger.LogDebug("Detected Yarn v2+ format (YAML)");
                parsedPackages = ParseYarnV2Lock(content);
            }
            else
            {
                logger.LogDebug("Detected Yarn v1 format");
                parsedPackages = ParseYarnV1Lock(content);
            }

            logger.LogInformation("Successfully parsed yarn.lock with {PackageCount} packages", parsedPackages.Count);

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
                packageInfo.TrackAsUsedInProject(yarnLockPath.Directory);

                // Fetch additional metadata from NPM registry since Yarn lock doesn't include license
                await metadataFetcher.FetchMetadataAsync(packageInfo);

                logger.LogDebug("Added Yarn package {Name} {Version} with license {License}",
                    packageName, packageData.Version, packageInfo.License ?? "Unknown");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse yarn.lock from {Path}", yarnLockPath);
        }
    }

    /// <summary>
    /// Detects if the yarn.lock file is in Yarn v2+ format (YAML with __metadata).
    /// </summary>
    private bool IsYarnV2Format(string content)
    {
        // Yarn v2+ files have a __metadata section at the top
        return content.Contains("__metadata:");
    }

    /// <summary>
    /// Parses the Yarn v2+ YAML lock file format into a dictionary of packages.
    /// </summary>
    private Dictionary<string, YarnPackageData> ParseYarnV2Lock(string content)
    {
        var packages = new Dictionary<string, YarnPackageData>();

        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var lockFile = deserializer.Deserialize<Dictionary<string, object>>(content);

            foreach (var (key, value) in lockFile)
            {
                // Skip metadata and other special entries
                if (key.StartsWith("__") || value is not Dictionary<object, object> packageData)
                {
                    continue;
                }

                // Parse package descriptor like "express@npm:^4.18.2"
                string packageName = ParseYarnV2PackageKey(key);

                if (string.IsNullOrEmpty(packageName))
                {
                    continue;
                }

                string? version = null;

                if (packageData.TryGetValue("version", out object? versionObj))
                {
                    version = versionObj.ToString();
                }

                if (packageData.TryGetValue("resolution", out object? resolutionObj))
                {
                    string? resolution = resolutionObj.ToString();

                    // Extract version from resolution if not present
                    // Format: "package@npm:version"
                    if (version == null && resolution != null)
                    {
                        var resMatch = Regex.Match(resolution, @"@npm:(.+)$");
                        if (resMatch.Success)
                        {
                            version = resMatch.Groups[1].Value;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(version))
                {
                    // For Yarn v2, construct resolved URL from npm registry
                    string resolvedUrl = $"https://registry.yarnpkg.com/{packageName}/-/{packageName.Split('/').Last()}-{version}.tgz";

                    packages[packageName] = new YarnPackageData
                    {
                        Version = version,
                        Resolved = resolvedUrl
                    };
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse Yarn v2 lock file");
        }

        return packages;
    }

    /// <summary>
    /// Parses a Yarn v2 package key to extract the package name.
    /// Format: "package@npm:^version" or "@scope/package@npm:^version"
    /// </summary>
    private string ParseYarnV2PackageKey(string key)
    {
        // Remove quotes if present
        key = key.Trim('"', '\'');

        // Handle scoped packages: @scope/package@npm:^version
        if (key.StartsWith("@"))
        {
            // Find the second @ which separates name from descriptor
            int secondAtIndex = key.IndexOf('@', 1);
            if (secondAtIndex > 0)
            {
                string packageName = key.Substring(0, secondAtIndex);
                return packageName;
            }
        }
        else
        {
            // Regular package: package@npm:^version
            int atIndex = key.IndexOf('@');
            if (atIndex > 0)
            {
                string packageName = key.Substring(0, atIndex);
                return packageName;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Parses the Yarn v1 custom text lock file format into a dictionary of packages.
    /// </summary>
    private Dictionary<string, YarnPackageData> ParseYarnV1Lock(string content)
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
