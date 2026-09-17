using System;
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
            UseCaching = true,
            NpmExePath = "npm.exe",
            ScanNuGet = false,
            GitHubApiKey = "token",
            RefreshRiskCache = true,
            RiskCacheMaxAgeHours = 6
        };

        AnalyzerSettings coreSettings = settings.ToCoreSettings();

        coreSettings.ForceRestore.Should().BeTrue();
        coreSettings.SkipRestore.Should().BeTrue();
        coreSettings.InteractiveRestore.Should().BeFalse();
        coreSettings.CacheFilePath.Should().Be("cache.bin");
        coreSettings.UseCaching.Should().BeTrue();
        coreSettings.NpmExePath.Should().Be("npm.exe");
        coreSettings.ScanNuGet.Should().BeFalse();
        coreSettings.GitHubApiKey.Should().Be("token");
        coreSettings.RefreshRiskCache.Should().BeTrue();
        coreSettings.RiskCacheMaxAge.Should().Be(TimeSpan.FromHours(6));
    }

    [TestMethod]
    public void Never_produces_a_negative_risk_cache_max_age()
    {
        var settings = new ExplainCommandSettings { RiskCacheMaxAgeHours = -5 };

        AnalyzerSettings coreSettings = settings.ToCoreSettings();

        coreSettings.RiskCacheMaxAge.Should().Be(TimeSpan.Zero);
    }
}
