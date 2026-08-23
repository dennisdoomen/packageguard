using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Package;
using PackageGuard.Core.Risk;
using PackageGuard.Core.Risk.Enrichment;

namespace PackageGuard.Specs;

[TestClass]
public class ParallelPackageRiskEnricherSpecs
{
    // GitHubRepositoryRiskEnricher swallows most exceptions and just leaves
    // HasGitHubRiskData false, so these two live-network tests are otherwise
    // undiagnosable when GitHub API calls intermittently fail in CI (rate
    // limiting, transient errors, etc.). A real logger surfaces the actual
    // status code/exception in the test output instead of a bare "found False".
    private static readonly ILogger DiagnosticLogger = LoggerFactory
        .Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Trace))
        .CreateLogger("GitHubDiagnostics");

    /// <summary>
    /// The token the live GitHub tests authenticate with, when one is configured.
    /// </summary>
    private static readonly string GitHubApiKey = Environment.GetEnvironmentVariable("GITHUB_API_KEY");

    [TestMethod]
    public async Task Skips_enrichers_that_already_have_cached_data()
    {
        // Arrange
        var skippedEnricher = new FakeRiskEnricher(_ => true);
        var executedEnricher = new FakeRiskEnricher(_ => false);
        var enricher = new ParallelPackageRiskEnricher(skippedEnricher, executedEnricher);

        var package = new PackageInfo
        {
            Name = "Bogus",
            Version = "1.0.0"
        };

        // Act
        await enricher.EnrichAsync([package]);

        // Assert
        skippedEnricher.EnrichedPackages.Should().BeEmpty();
        executedEnricher.EnrichedPackages.Should().ContainSingle().Which.Should().Be(package);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Osv_enricher_should_populate_vulnerability_data_for_a_real_package()
    {
        var enricher = new OsvRiskEnricher(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "Newtonsoft.Json",
            Version = "13.0.3",
            Source = "NuGet"
        };

        await enricher.EnrichAsync(package);

        package.HasOsvRiskData.Should().BeTrue();
        package.VulnerabilityCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task License_url_enricher_should_validate_a_real_license_url()
    {
        var enricher = new LicenseUrlRiskEnricher(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "Newtonsoft.Json",
            Version = "13.0.3",
            LicenseUrl = "https://licenses.nuget.org/MIT"
        };

        await enricher.EnrichAsync(package);

        package.HasValidatedLicenseUrl.Should().BeTrue();
        package.HasValidLicenseUrl.Should().BeTrue();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GitHub_enricher_should_populate_repository_data_for_a_well_known_package()
    {
        using var client = new GitHubApiClient(DiagnosticLogger, GitHubApiKey);
        var enricher = new GitHubRepositoryRiskEnricher(DiagnosticLogger, client);
        var package = new PackageInfo
        {
            Name = "Newtonsoft.Json",
            Version = "13.0.3",
            RepositoryUrl = "https://github.com/JamesNK/Newtonsoft.Json"
        };

        await enricher.EnrichAsync(package);

        SkipWhenGitHubRefusedToAnswer(client);

        package.HasGitHubRiskData.Should().BeTrue();
        package.ContributorCount.Should().BeGreaterThan(0);
        package.HasReadme.Should().BeTrue();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Osv_enricher_should_detect_vulnerabilities_and_fix_data_for_a_known_vulnerable_version()
    {
        var enricher = new OsvRiskEnricher(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "Newtonsoft.Json",
            Version = "12.0.3",
            Source = "NuGet"
        };

        await enricher.EnrichAsync(package);

        package.HasOsvRiskData.Should().BeTrue();
        package.VulnerabilityCount.Should().BeGreaterThan(0, "Newtonsoft.Json 12.0.3 has known CVEs in the OSV database");
        package.HasAvailableSecurityFix.Should().BeTrue("a patched version exists for this vulnerability");
        package.Vulnerabilities.Should().HaveCount(package.VulnerabilityCount,
            "every aggregated vulnerability count should have a matching individual record for SBOM reporting");
        package.Vulnerabilities.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v.Id));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Osv_enricher_should_query_npm_ecosystem_for_npm_packages()
    {
        var enricher = new OsvRiskEnricher(NullLogger.Instance);
        var package = new PackageInfo
        {
            Name = "lodash",
            Version = "4.17.11",
            Source = "npm"
        };

        await enricher.EnrichAsync(package);

        package.HasOsvRiskData.Should().BeTrue();
        package.VulnerabilityCount.Should().BeGreaterThan(0, "lodash 4.17.11 has known CVEs in the OSV database");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Full_enrichment_pipeline_should_populate_all_network_risk_signals_for_a_real_package()
    {
        var enricher = new ParallelPackageRiskEnricher(DiagnosticLogger, GitHubApiKey);
        var package = new PackageInfo
        {
            Name = "Newtonsoft.Json",
            Version = "13.0.3",
            Source = "NuGet",
            RepositoryUrl = "https://github.com/JamesNK/Newtonsoft.Json",
            LicenseUrl = "https://licenses.nuget.org/MIT"
        };

        await enricher.EnrichAsync([package]);

        package.HasOsvRiskData.Should().BeTrue();
        package.HasValidatedLicenseUrl.Should().BeTrue();

        SkipWhenGitHubRefusedToAnswer(GitHubApi.GetOrCreateClient(DiagnosticLogger, GitHubApiKey));

        package.HasGitHubRiskData.Should().BeTrue();
    }

    /// <summary>
    /// Reports the test as inconclusive when GitHub stopped answering because the rate limit budget ran out.
    /// </summary>
    /// <remarks>
    /// These tests talk to the live API. Runners share an IP, so an unauthenticated run competes for 60 requests an
    /// hour with everything else on that address. Asserting on signals GitHub declined to hand over would report a
    /// spent budget as a defect in the code under test.
    /// </remarks>
    private static void SkipWhenGitHubRefusedToAnswer(GitHubApiClient client)
    {
        if (client.IsExhausted)
        {
            Assert.Inconclusive("Skipped because the GitHub API rate limit is exhausted. Set GITHUB_API_KEY to a " +
                "personal access token to raise the limit from 60 to 5000 requests per hour.");
        }
    }

    private sealed class FakeRiskEnricher(Func<PackageInfo, bool> hasCachedData) : IEnrichPackageRisk
    {
        public List<PackageInfo> EnrichedPackages { get; } = [];

        public bool HasCachedData(PackageInfo package) => hasCachedData(package);

        public Task EnrichAsync(PackageInfo package)
        {
            EnrichedPackages.Add(package);
            return Task.CompletedTask;
        }
    }
}
