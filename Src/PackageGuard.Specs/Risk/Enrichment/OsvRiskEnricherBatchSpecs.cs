using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Package;
using PackageGuard.Core.Risk.Enrichment;
using PackageGuard.Specs.Common;

namespace PackageGuard.Specs.Risk.Enrichment;

[TestClass]
[DoNotParallelize]
public class OsvRiskEnricherBatchSpecs
{
    [TestInitialize]
    public void ClearSharedState() => OsvRiskEnricher.ClearCache();

    [TestCleanup]
    public void ClearSharedStateAfterwards() => OsvRiskEnricher.ClearCache();

    [TestMethod]
    public async Task Looks_up_every_package_in_a_single_batch_request()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((_, _) => ScriptedResponse.Json(EmptyBatchResults(50)));
        var enricher = new OsvRiskEnricher(NullLogger.Instance, new HttpClient(handler));
        PackageInfo[] packages = CreatePackages(50);

        // Act
        await enricher.PrimeAsync(packages);
        foreach (PackageInfo package in packages)
        {
            await enricher.EnrichAsync(package);
        }

        // Assert
        handler.RequestCount.Should().Be(1, "one batch answers for all fifty packages");
        packages.Should().OnlyContain(package => package.HasOsvRiskData);
        packages.Should().OnlyContain(package => package.VulnerabilityCount == 0);
    }

    [TestMethod]
    public async Task Fetches_the_detail_of_each_vulnerability_the_batch_turned_up()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.Contains("/vulns/")
                ? ScriptedResponse.Json(Vulnerability)
                : ScriptedResponse.Json(BatchResultsWithOneVulnerability));

        var enricher = new OsvRiskEnricher(NullLogger.Instance, new HttpClient(handler));
        PackageInfo[] packages = CreatePackages(3);

        // Act
        await enricher.PrimeAsync(packages);
        foreach (PackageInfo package in packages)
        {
            await enricher.EnrichAsync(package);
        }

        // Assert
        handler.RequestCount.Should().Be(2, "the batch plus the one vulnerability it shares between packages");
        packages[0].VulnerabilityCount.Should().Be(1);
        packages[0].MaxVulnerabilitySeverity.Should().BeApproximately(9.8, 0.01);
        packages[1].VulnerabilityCount.Should().Be(0);
    }

    [TestMethod]
    public async Task Falls_back_to_a_single_query_when_the_batch_result_is_truncated()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/querybatch")
                ? ScriptedResponse.Json(TruncatedBatchResults)
                : ScriptedResponse.Json("""{"vulns":[]}"""));

        var enricher = new OsvRiskEnricher(NullLogger.Instance, new HttpClient(handler));
        PackageInfo package = CreatePackages(1).Single();

        // Act
        await enricher.PrimeAsync([package]);
        await enricher.EnrichAsync(package);

        // Assert
        package.HasOsvRiskData.Should().BeTrue();
        handler.RequestedUrls.Should().Contain(url => url.EndsWith("/v1/query"));
    }

    private static PackageInfo[] CreatePackages(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new PackageInfo
            {
                Name = $"Acme.Package{index}",
                Version = "1.0.0",
                Source = "NuGet"
            })
            .ToArray();

    private static string EmptyBatchResults(int count) =>
        $$"""{"results":[{{string.Join(",", Enumerable.Repeat("{}", count))}}]}""";

    private const string BatchResultsWithOneVulnerability =
        """
        {"results":[{"vulns":[{"id":"GHSA-1111-2222-3333","modified":"2026-01-01T00:00:00Z"}]},{},{}]}
        """;

    private const string TruncatedBatchResults =
        """
        {"results":[{"vulns":[{"id":"GHSA-1111-2222-3333"}],"next_page_token":"more"}]}
        """;

    private const string Vulnerability =
        """
        {
          "id": "GHSA-1111-2222-3333",
          "modified": "2026-01-01T00:00:00Z",
          "aliases": ["CVE-2026-0001"],
          "severity": [{"type": "CVSS_V3", "score": "9.8"}],
          "references": [{"url": "https://example.com/advisory"}]
        }
        """;
}
