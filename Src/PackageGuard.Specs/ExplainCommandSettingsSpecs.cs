using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
public class ExplainCommandSettingsSpecs
{
    [TestMethod]
    public void Always_enables_risk_reporting_regardless_of_the_flags_passed()
    {
        var settings = new ExplainCommandSettings();

        AnalyzerSettings coreSettings = settings.ToCoreSettings();

        coreSettings.ReportRisk.Should().BeTrue();
    }

    [TestMethod]
    public void Maps_restore_caching_and_npm_options_onto_the_core_settings()
    {
        var settings = new ExplainCommandSettings
        {
            ForceRestore = true,
            SkipRestore = true,
            Interactive = false,
            CacheFilePath = "cache.bin",
            NpmExePath = "npm.exe",
            ScanNuGet = false,
            GitHubApiKey = "token"
        };

        AnalyzerSettings coreSettings = settings.ToCoreSettings();

        coreSettings.ForceRestore.Should().BeTrue();
        coreSettings.SkipRestore.Should().BeTrue();
        coreSettings.InteractiveRestore.Should().BeFalse();
        coreSettings.CacheFilePath.Should().Be("cache.bin");
        coreSettings.NpmExePath.Should().Be("npm.exe");
        coreSettings.ScanNuGet.Should().BeFalse();
        coreSettings.GitHubApiKey.Should().Be("token");
    }

    [TestMethod]
    public void Always_reads_the_cache_but_never_updates_it()
    {
        AnalyzerSettings coreSettings = new ExplainCommandSettings().ToCoreSettings();

        coreSettings.UseCaching.Should().BeTrue();
        coreSettings.ReadOnlyCache.Should().BeTrue();
    }
}
