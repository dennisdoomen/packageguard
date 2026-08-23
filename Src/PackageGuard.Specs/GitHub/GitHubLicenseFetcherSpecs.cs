using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.CSharp.FetchingStrategies;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Package;
using PackageGuard.Specs.Common;

namespace PackageGuard.Specs.GitHub;

[TestClass]
public class GitHubLicenseFetcherSpecs
{
    private const string LicenseBody = """{"license":{"spdx_id":"MIT"}}""";

    [TestMethod]
    public async Task Resolves_the_spdx_identifier_of_a_github_repository()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json(LicenseBody));
        PackageInfo package = CreatePackage();

        // Act
        await FetchAsync(handler, package);

        // Assert
        package.License.Should().Be("MIT");
        package.LicenseEvidence.Should().Be(LicenseEvidence.Concluded);
    }

    [TestMethod]
    public async Task Treats_a_repository_without_an_asserted_license_as_unresolved()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() =>
            ScriptedResponse.Json("""{"license":{"spdx_id":"NOASSERTION"}}"""));

        PackageInfo package = CreatePackage();

        // Act
        await FetchAsync(handler, package);

        // Assert
        package.License.Should().BeNull();
    }

    [TestMethod]
    public async Task Retries_a_license_lookup_that_hits_the_secondary_rate_limit()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((_, attempt) => attempt == 1
            ? ScriptedResponse.SecondaryRateLimited()
            : ScriptedResponse.Json(LicenseBody));

        PackageInfo package = CreatePackage();

        // Act
        await FetchAsync(handler, package);

        // Assert
        package.License.Should().Be("MIT", "the retry succeeded");
        handler.RequestCount.Should().Be(2);
    }

    [TestMethod]
    public async Task Leaves_the_license_unresolved_once_the_rate_limit_budget_is_spent()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() =>
            ScriptedResponse.PrimaryRateLimited(TimeSpan.FromMinutes(45)));

        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var fetcher = new GitHubLicenseFetcher(NullLogger.Instance, client);

        // Act
        await fetcher.FetchLicenseAsync(CreatePackage());
        await fetcher.FetchLicenseAsync(CreatePackage("Contoso.Gadget", "https://github.com/contoso/gadget"));

        // Assert
        handler.RequestCount.Should().Be(1, "the second package should not be attempted at all");
    }

    [TestMethod]
    public async Task Ignores_a_package_whose_repository_is_not_on_github()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json(LicenseBody));
        PackageInfo package = CreatePackage("Acme.Widget", "https://gitlab.com/acme/widget");

        // Act
        await FetchAsync(handler, package);

        // Assert
        package.License.Should().BeNull();
        handler.RequestCount.Should().Be(0);
    }

    [TestMethod]
    public async Task Reads_the_license_from_the_repository_resource_that_risk_enrichment_needs_as_well()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json(LicenseBody));

        // Act
        await FetchAsync(handler, CreatePackage());

        // Assert
        handler.RequestedUrls.Single().Should().Be("https://api.github.com/repos/acme/widget",
            "asking the dedicated license endpoint would spend a request on what this response already carries");
    }

    [TestMethod]
    public async Task Strips_the_git_suffix_from_a_repository_url()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json(LicenseBody));

        // Act
        await FetchAsync(handler, CreatePackage("Acme.Widget", "https://github.com/acme/widget.git"));

        // Assert
        handler.RequestedUrls.Single().Should().Be("https://api.github.com/repos/acme/widget");
    }

    private static async Task FetchAsync(ScriptedHttpMessageHandler handler, PackageInfo package)
    {
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        var fetcher = new GitHubLicenseFetcher(NullLogger.Instance, client);
        await fetcher.FetchLicenseAsync(package);
    }

    private static PackageInfo CreatePackage(string name = "Acme.Widget",
        string repositoryUrl = "https://github.com/acme/widget") =>
        new()
        {
            Name = name,
            Version = "1.0.0",
            Source = "NuGet",
            RepositoryUrl = repositoryUrl
        };
}
