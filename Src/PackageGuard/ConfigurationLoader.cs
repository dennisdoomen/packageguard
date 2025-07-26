using Microsoft.Extensions.Configuration;
using PackageGuard.Core;

namespace PackageGuard;

public static class ConfigurationLoader
{
    public static void Configure(CSharpProjectAnalyzer analyzer, string configurationPath)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configurationPath, optional: true)
            .AddEnvironmentVariables()
            .Build();

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
