using Microsoft.Extensions.Configuration;
using PackageGuard.Core;
using Pathy;

namespace PackageGuard;

public static class ConfigurationLoader
{
    /// <summary>
    /// Configures the analyzer using hierarchical configuration discovery.
    /// Looks for configuration files at solution and project levels.
    /// </summary>
    public static void ConfigureHierarchical(CSharpProjectAnalyzer analyzer, string projectPath)
    {
        var configPaths = DiscoverConfigurationFiles(projectPath);
        
        if (configPaths.Count == 0)
        {
            // No configuration files found, use empty configuration
            return;
        }

        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddEnvironmentVariables();

        // Add configuration files in order from most general to most specific
        // This ensures that project-level settings override solution-level settings
        foreach (var configPath in configPaths)
        {
            configurationBuilder.AddJsonFile(configPath, optional: true);
        }

        var configuration = configurationBuilder.Build();
        ApplyConfiguration(analyzer, configuration);
    }

    /// <summary>
    /// Configures the analyzer using a single configuration file.
    /// This is the original behavior for backward compatibility.
    /// </summary>
    public static void Configure(CSharpProjectAnalyzer analyzer, string configurationPath)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configurationPath, optional: true)
            .AddEnvironmentVariables()
            .Build();

        ApplyConfiguration(analyzer, configuration);
    }

    /// <summary>
    /// Discovers configuration files in hierarchical order from solution to project level.
    /// </summary>
    private static List<string> DiscoverConfigurationFiles(string projectPath)
    {
        var configFiles = new List<string>();
        ChainablePath pathy = string.IsNullOrEmpty(projectPath) ? ChainablePath.Current : projectPath;

        // If projectPath points to a specific file, get its directory
        if (pathy.IsFile)
        {
            pathy = pathy.Directory;
        }

        // Find solution directory and config files
        var solutionDirectory = FindSolutionDirectory(pathy);
        if (solutionDirectory.HasValue)
        {
            AddConfigFilesFromDirectory(configFiles, solutionDirectory.Value);
        }

        // If we're analyzing a specific project directory that's different from solution directory,
        // also add config files from the project directory
        if (solutionDirectory.HasValue && !pathy.Equals(solutionDirectory.Value))
        {
            AddConfigFilesFromDirectory(configFiles, pathy);
        }

        return configFiles;
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
        var globalSettings = configuration.GetSection("Settings").Get<GlobalSettings>() ?? new GlobalSettings();

        foreach (string package in globalSettings.Allow.Packages)
        {
            string[] segments = package.Split("/");

            analyzer.AllowList.Packages.Add(new PackageSelector(segments[0], segments.ElementAtOrDefault(1) ?? ""));
        }

        analyzer.AllowList.Licenses.AddRange(globalSettings.Allow.Licenses);
        analyzer.AllowList.Feeds.AddRange(globalSettings.Allow.Feeds);
        analyzer.AllowList.Prerelease = globalSettings.Allow.Prerelease;

        foreach (string package in globalSettings.Deny.Packages)
        {
            string[] segments = package.Split("/");

            analyzer.DenyList.Packages.Add(new PackageSelector(segments[0], segments.ElementAtOrDefault(1) ?? ""));
        }

        analyzer.DenyList.Licenses.AddRange(globalSettings.Deny.Licenses);
        analyzer.DenyList.Prerelease = globalSettings.Deny.Prerelease;

        analyzer.IgnoredFeeds = globalSettings.IgnoredFeeds;
    }
}
