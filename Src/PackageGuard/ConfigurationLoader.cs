using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PackageGuard.Core;
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

        // Load and merge configurations manually to ensure proper accumulation
        var mergedSettings = new PolicySettings();

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
                mergedSettings.MergeWith(settings);
            }
        }

        return ToPolicy(mergedSettings);
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
        return ToPolicy(settings);
    }

    private static ProjectPolicy ToPolicy(PolicySettings settings)
    {
        var policy = new ProjectPolicy
        {
            AllowList = new AllowList(),
            DenyList = new DenyList()
        };

        foreach (string package in settings.Allow.Packages)
        {
            string[] segments = package.Split("/");
            policy.AllowList.Packages.Add(new PackageSelector(segments[0], segments.ElementAtOrDefault(1) ?? ""));
        }

        policy.AllowList.Licenses.AddRange(settings.Allow.Licenses);
        policy.AllowList.Feeds.AddRange(settings.Allow.Feeds);
        policy.AllowList.Prerelease = settings.Allow.Prerelease;

        foreach (string package in settings.Deny.Packages)
        {
            string[] segments = package.Split("/");
            policy.DenyList.Packages.Add(new PackageSelector(segments[0], segments.ElementAtOrDefault(1) ?? ""));
        }

        policy.DenyList.Licenses.AddRange(settings.Deny.Licenses);
        policy.DenyList.Prerelease = settings.Deny.Prerelease;

        policy.IgnoredFeeds = settings.IgnoredFeeds;

        return policy;
    }
}
