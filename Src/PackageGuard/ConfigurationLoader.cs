using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PackageGuard.Core;
using PackageGuard.Core.Policy;
using Pathy;

namespace PackageGuard;

public class ConfigurationLoader(ILogger logger)
{
    /// <summary>
    /// Gets the effective configuration for a specific project by merging solution-level and project-level configurations.
    /// </summary>
    /// <param name="projectPath">Path to the specific project directory or file</param>
    /// <returns>Merged GlobalSettings for the project</returns>
    public ProjectPolicy GetEffectiveConfigurationForProject(string projectPath)
    {
        List<string> configPaths = DiscoverConfigurationFiles(projectPath);

        if (configPaths.Count == 0)
        {
            // No configuration files found, return empty configuration
            return new ProjectPolicy();
        }

        var merged = new ProjectPolicy
        {
            AllowList = new AllowList(),
            DenyList = new DenyList()
        };

        foreach (var configPath in configPaths)
        {
            logger.LogInformation("Appending the policies from {Path}", ChainablePath.From(configPath));

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(configPath, optional: true)
                .Build();

            var settings = configuration.GetSection("Settings").Get<PolicySettings>();
            if (settings != null)
            {
                MergeInto(merged, ToPolicy(settings, configPath));
            }
        }

        return merged;
    }

    /// <summary>
    /// Merges the packages, licenses, feeds, and prerelease flags from <paramref name="source"/> into
    /// <paramref name="target"/>, preserving which configuration file each rule was first introduced by.
    /// </summary>
    private static void MergeInto(ProjectPolicy target, ProjectPolicy source)
    {
        MergePackagePolicy(target.AllowList, source.AllowList);
        target.AllowList.Feeds.AddRange(source.AllowList.Feeds);
        foreach ((string feed, string sourceFile) in source.AllowList.FeedSourceFiles)
        {
            target.AllowList.FeedSourceFiles.TryAdd(feed, sourceFile);
        }

        target.AllowList.Prerelease = source.AllowList.Prerelease;

        MergePackagePolicy(target.DenyList, source.DenyList);
        target.DenyList.Prerelease = source.DenyList.Prerelease;

        target.IgnoredFeeds = [..target.IgnoredFeeds, ..source.IgnoredFeeds];
    }

    /// <summary>
    /// Merges the packages and licenses from <paramref name="source"/> into <paramref name="target"/>,
    /// keeping the first configuration file that introduced each license entry.
    /// </summary>
    private static void MergePackagePolicy(PackagePolicy target, PackagePolicy source)
    {
        target.Packages.AddRange(source.Packages);
        target.Licenses.AddRange(source.Licenses);

        foreach ((string license, string sourceFile) in source.LicenseSourceFiles)
        {
            target.LicenseSourceFiles.TryAdd(license, sourceFile);
        }
    }

    /// <summary>
    /// Discovers configuration files for a specific project in hierarchical order.
    /// Returns configuration files from:
    /// 1. Solution level (if found)
    /// 2. The specific project level only (not sibling projects)
    /// </summary>
    /// <param name="projectPath">Path to the specific project directory or file</param>
    private static List<string> DiscoverConfigurationFiles(string projectPath)
    {
        var configFiles = new List<string>();
        ChainablePath path = string.IsNullOrEmpty(projectPath) ? ChainablePath.Current : projectPath;

        // If projectPath points to a specific file, get its directory
        if (path.IsFile)
        {
            path = path.Directory;
        }

        // Find solution directory and add its config files
        ChainablePath solutionDirectory = path.FindParentWithFileMatching("*.sln", "*.slnx");
        if (!solutionDirectory.IsNull)
        {
            AddConfigFilesFromDirectory(configFiles, solutionDirectory);
        }

        // Add config files from the specific project directory (if different from solution directory)
        // Note: This only adds configs for the specified project, not sibling projects
        if (solutionDirectory.IsNull || !path.Equals(solutionDirectory))
        {
            AddConfigFilesFromDirectory(configFiles, path);
        }

        return configFiles;
    }

    /// <summary>
    /// Adds configuration files from a specific directory if they exist.
    /// </summary>
    private static void AddConfigFilesFromDirectory(List<string> configFiles, ChainablePath directory)
    {
        // Check for packageguard.config.json in the directory
        var packageGuardConfig = directory / "packageguard.config.json";
        if (packageGuardConfig.IsFile)
        {
            configFiles.Add(packageGuardConfig);
        }

        // Check for config.json in .packageguard subdirectory
        var dotPackageGuardConfig = directory / ".packageguard" / "config.json";
        if (dotPackageGuardConfig.IsFile)
        {
            configFiles.Add(dotPackageGuardConfig);
        }
    }

    /// <summary>
    /// Configures the analyzer using a single configuration file.
    /// This is the original behavior for backward compatibility.
    /// </summary>
    public ProjectPolicy GetConfigurationFromConfigPath(string configurationPath)
    {
        logger.LogInformation("Loading the policies from {Path}", ChainablePath.From(configurationPath));

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configurationPath, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var settings = configuration.GetSection("Settings").Get<PolicySettings>() ?? new PolicySettings();
        return ToPolicy(settings, configurationPath);
    }

    /// <summary>
    /// Converts the given <paramref name="settings"/> into a <see cref="ProjectPolicy"/>, stamping every
    /// rule it defines with <paramref name="sourceFile"/> so later merging and the <c>explain</c> command
    /// can report which configuration file a rule came from.
    /// </summary>
    private static ProjectPolicy ToPolicy(PolicySettings settings, string sourceFile)
    {
        var policy = new ProjectPolicy
        {
            AllowList = new AllowList(),
            DenyList = new DenyList()
        };

        foreach (string package in settings.Allow.Packages)
        {
            string[] segments = package.Split("/");
            policy.AllowList.Packages.Add(new PackageSelector(segments[0], segments.ElementAtOrDefault(1) ?? "")
            {
                SourceFile = sourceFile
            });
        }

        policy.AllowList.Licenses.AddRange(settings.Allow.Licenses);
        foreach (string license in settings.Allow.Licenses)
        {
            policy.AllowList.LicenseSourceFiles[license] = sourceFile;
        }

        policy.AllowList.Feeds.AddRange(settings.Allow.Feeds);
        foreach (string feed in settings.Allow.Feeds)
        {
            policy.AllowList.FeedSourceFiles[feed] = sourceFile;
        }

        policy.AllowList.Prerelease = settings.Allow.Prerelease;

        foreach (string package in settings.Deny.Packages)
        {
            string[] segments = package.Split("/");
            policy.DenyList.Packages.Add(new PackageSelector(segments[0], segments.ElementAtOrDefault(1) ?? "")
            {
                SourceFile = sourceFile
            });
        }

        policy.DenyList.Licenses.AddRange(settings.Deny.Licenses);
        foreach (string license in settings.Deny.Licenses)
        {
            policy.DenyList.LicenseSourceFiles[license] = sourceFile;
        }

        policy.DenyList.Prerelease = settings.Deny.Prerelease;

        policy.IgnoredFeeds = settings.IgnoredFeeds;

        return policy;
    }
}
