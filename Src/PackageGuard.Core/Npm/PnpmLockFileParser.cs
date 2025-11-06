using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pathy;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Loads and parses Pnpm pnpm-lock.yaml files to extract package information.
/// </summary>
internal class PnpmLockFileParser
{
    private readonly NpmRegistryMetadataFetcher metadataFetcher;

    private readonly ILogger logger;

    public PnpmLockFileParser(ILogger? logger = null)
    {
        this.logger = logger ?? NullLogger.Instance;
        metadataFetcher = new NpmRegistryMetadataFetcher(this.logger);
    }

    /// <summary>
    /// Parses a Pnpm pnpm-lock.yaml file and populates a <see cref="PackageInfoCollection"/> with the packages found.
    /// </summary>
    /// <param name="pnpmLockPath">The full path to the pnpm-lock.yaml file.</param>
    /// <param name="packages">The collection to populate with package information.</param>
    public async Task CollectPackageMetadata(ChainablePath pnpmLockPath, PackageInfoCollection packages)
    {
        if (!File.Exists(pnpmLockPath))
        {
            logger.LogWarning("pnpm-lock.yaml file not found at {Path}", pnpmLockPath);
            return;
        }

        try
        {
            logger.LogInformation("Loading Pnpm lock file from {Path}", pnpmLockPath);

            string yamlContent = File.ReadAllText(pnpmLockPath);

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var lockFile = deserializer.Deserialize<PnpmLockFile>(yamlContent);

            if (lockFile.Packages == null)
            {
                logger.LogWarning("No packages found in pnpm-lock.yaml at {Path}", pnpmLockPath);
                return;
            }

            logger.LogInformation("Successfully parsed pnpm-lock.yaml with {PackageCount} packages", lockFile.Packages.Count);

            foreach (var (packageKey, packageData) in lockFile.Packages)
            {
                // Package key format: /package-name@version or /@scope/package-name@version
                var (packageName, version) = ParsePackageKey(packageKey);

                if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(version))
                {
                    logger.LogDebug("Skipping invalid package key: {Key}", packageKey);
                    continue;
                }

                // Extract resolved URL from resolution
                string? resolvedUrl = packageData.Resolution?.Integrity != null
                    ? $"https://registry.npmjs.org/{packageName}/-/{packageName.Split('/').Last()}-{version}.tgz"
                    : null;

                var packageInfo = new PackageInfo
                {
                    Name = packageName,
                    Version = version,
                    License = null, // Pnpm lock files typically don't include license info
                    Source = "npm",
                    SourceUrl = resolvedUrl ?? "https://registry.npmjs.org"
                };

                packages.Add(packageInfo);
                packageInfo.TrackAsUsedInProject(pnpmLockPath.Directory);

                // Fetch additional metadata from NPM registry since Pnpm lock doesn't include license
                await metadataFetcher.FetchMetadataAsync(packageInfo);

                logger.LogDebug("Added Pnpm package {Name} {Version} with license {License}",
                    packageName, version, packageInfo.License ?? "Unknown");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse pnpm-lock.yaml from {Path}", pnpmLockPath);
        }
    }

    /// <summary>
    /// Parses a pnpm package key to extract the package name and version.
    /// Format: /package-name@version or /@scope/package-name@version
    /// </summary>
    private (string packageName, string version) ParsePackageKey(string packageKey)
    {
        // Remove leading slash
        if (packageKey.StartsWith("/"))
        {
            packageKey = packageKey.Substring(1);
        }

        // Handle scoped packages: @scope/package-name@version
        if (packageKey.StartsWith("@"))
        {
            // Find the last @ which separates name from version
            int lastAtIndex = packageKey.LastIndexOf('@');
            if (lastAtIndex > 0)
            {
                string packageName = packageKey.Substring(0, lastAtIndex);
                string version = packageKey.Substring(lastAtIndex + 1);

                // Remove any additional path info (like (peer-deps) suffix)
                int parenIndex = version.IndexOf('(');
                if (parenIndex > 0)
                {
                    version = version.Substring(0, parenIndex);
                }

                return (packageName, version);
            }
        }
        else
        {
            // Regular package: package-name@version
            int atIndex = packageKey.IndexOf('@');
            if (atIndex > 0)
            {
                string packageName = packageKey.Substring(0, atIndex);
                string version = packageKey.Substring(atIndex + 1);

                // Remove any additional path info
                int parenIndex = version.IndexOf('(');
                if (parenIndex > 0)
                {
                    version = version.Substring(0, parenIndex);
                }

                return (packageName, version);
            }
        }

        return (string.Empty, string.Empty);
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private class PnpmLockFile
    {
        public Dictionary<string, PnpmPackageData>? Packages { get; set; }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private class PnpmPackageData
    {
        public PnpmResolution? Resolution { get; set; }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private class PnpmResolution
    {
        public string? Integrity { get; set; }
    }
}
