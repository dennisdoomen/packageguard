using Microsoft.Extensions.Configuration;
using PackageGuard.Core;
using Pathy;

namespace PackageGuard;

public static class ConfigurationLoader
{
    /// <summary>
    /// Configures the analyzer using hierarchical configuration discovery for a specific project.
    /// This method loads and merges configuration files in the following order:
    /// 1. Solution-level configuration files (if a solution is found)
    /// 2. The specific project's configuration files (if different from solution directory)
    ///
    /// Note: Only configurations for the specified project are loaded - configurations
    /// from sibling projects are NOT included in the merge.
    /// </summary>
    /// <param name="analyzer">The analyzer to configure</param>
    /// <param name="projectPath">Path to the specific project directory or project file</param>
    public static void ConfigureHierarchical(CSharpProjectAnalyzer analyzer, string projectPath)
    {
        PackagePolicy policy = GetEffectiveConfigurationForProject(projectPath);
        ApplyPolicy(analyzer, policy);
    }

    /// <summary>
    /// Gets the effective configuration for a specific project by merging solution-level and project-level configurations.
    /// </summary>
    /// <param name="projectPath">Path to the specific project directory or file</param>
    /// <returns>Merged GlobalSettings for the project</returns>
    public static PackagePolicy GetEffectiveConfigurationForProject(string projectPath)
    {
        List<string> configPaths = DiscoverConfigurationFiles(projectPath);

        if (configPaths.Count == 0)
        {
            // No configuration files found, return empty configuration
            return new PackagePolicy();
        }

        // Load and merge configurations manually to ensure proper accumulation
        var mergedSettings = new PackagePolicy();

        foreach (var configPath in configPaths)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(configPath, optional: true)
                .Build();

            var settings = configuration.GetSection("Settings").Get<PackagePolicy>();
            if (settings != null)
            {
                mergedSettings.MergeWith(settings);
            }
        }

        return mergedSettings;
    }

    /// <summary>
    /// Discovers configuration files for a specific project in hierarchical order.
    /// Returns configuration files from:
    /// 1. Solution level (if found)
    /// 2. The specific project level only (not sibling projects)
    /// </summary>
    /// <param name="projectPath">Path to the specific project directory or file</param>
    /// <returns>List of configuration file paths in order of precedence</returns>
    private static List<string> DiscoverConfigurationFiles(string projectPath)
    {
        var configFiles = new List<string>();
        ChainablePath pathy = string.IsNullOrEmpty(projectPath) ? ChainablePath.Current : projectPath;

        // If projectPath points to a specific file, get its directory
        if (pathy.IsFile)
        {
            pathy = pathy.Directory;
        }

        // Find solution directory and add its config files
        var solutionDirectory = FindSolutionDirectory(pathy);
        if (solutionDirectory.HasValue)
        {
            AddConfigFilesFromDirectory(configFiles, solutionDirectory.Value);
        }

        // Add config files from the specific project directory (if different from solution directory)
        // Note: This only adds configs for the specified project, not sibling projects
        if (solutionDirectory.HasValue && !pathy.Equals(solutionDirectory.Value))
        {
            AddConfigFilesFromDirectory(configFiles, pathy);
        }

        return configFiles;
    }

    /// <summary>
    /// Configures the analyzer using a single configuration file.
    /// This is the original behavior for backward compatibility.
    /// </summary>
    public static void ConfigureForBackwardsCompatibility(CSharpProjectAnalyzer analyzer, string configurationPath)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configurationPath, optional: true)
            .AddEnvironmentVariables()
            .Build();

        ApplyConfiguration(analyzer, configuration);
    }

    /// <summary>
    /// Finds the directory containing a solution file (.sln or .slnx).
    /// </summary>
    private static ChainablePath? FindSolutionDirectory(ChainablePath startPath)
    {
        var currentPath = startPath.ToString();

        // Look for solution files in the current directory and parent directories
        while (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
        {
            var solutionFiles = Directory.GetFiles(currentPath, "*.sln", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(currentPath, "*.slnx", SearchOption.TopDirectoryOnly))
                .ToArray();

            if (solutionFiles.Length > 0)
            {
                return currentPath;
            }

            var parent = Directory.GetParent(currentPath)?.FullName;
            if (string.IsNullOrEmpty(parent) || parent.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
            {
                break; // Reached root directory
            }
            currentPath = parent;
        }

        return null;
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
    /// Applies configuration settings to the analyzer.
    /// </summary>
    private static void ApplyConfiguration(CSharpProjectAnalyzer analyzer, IConfiguration configuration)
    {
        var globalSettings = configuration.GetSection("Settings").Get<PackagePolicy>() ?? new PackagePolicy();
        ApplyPolicy(analyzer, globalSettings);
    }

    /// <summary>
    /// Applies GlobalSettings to the analyzer.
    /// </summary>
    private static void ApplyPolicy(CSharpProjectAnalyzer analyzer, PackagePolicy packagePolicy)
    {
        foreach (string package in packagePolicy.Allow.Packages)
        {
            string[] segments = package.Split("/");
            analyzer.AllowList.Packages.Add(new PackageSelector(segments[0], segments.ElementAtOrDefault(1) ?? ""));
        }

        analyzer.AllowList.Licenses.AddRange(packagePolicy.Allow.Licenses);
        analyzer.AllowList.Feeds.AddRange(packagePolicy.Allow.Feeds);
        analyzer.AllowList.Prerelease = packagePolicy.Allow.Prerelease;

        foreach (string package in packagePolicy.Deny.Packages)
        {
            string[] segments = package.Split("/");
            analyzer.DenyList.Packages.Add(new PackageSelector(segments[0], segments.ElementAtOrDefault(1) ?? ""));
        }

        analyzer.DenyList.Licenses.AddRange(packagePolicy.Deny.Licenses);
        analyzer.DenyList.Prerelease = packagePolicy.Deny.Prerelease;

        analyzer.IgnoredFeeds = packagePolicy.IgnoredFeeds;
    }
}
