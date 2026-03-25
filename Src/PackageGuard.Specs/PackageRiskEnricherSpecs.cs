using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
public class PackageRiskEnricherSpecs
{
    [TestMethod]
    public async Task Skips_enrichers_that_already_have_cached_data()
    {
        // Arrange
        var skippedEnricher = new FakeRiskEnricher(_ => true);
        var executedEnricher = new FakeRiskEnricher(_ => false);
        var enricher = new PackageRiskEnricher(skippedEnricher, executedEnricher);

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

    private sealed class FakeRiskEnricher(System.Func<PackageInfo, bool> hasCachedData) : IEnrichPackageRisk
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
