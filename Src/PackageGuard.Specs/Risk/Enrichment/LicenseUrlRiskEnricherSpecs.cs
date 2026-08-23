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
public class LicenseUrlRiskEnricherSpecs
{
    private const string SharedLicenseUrl = "https://licenses.example.com/MIT.txt";

    [TestInitialize]
    public void ClearSharedState() => LicenseUrlRiskEnricher.ClearCache();

    [TestCleanup]
    public void ClearSharedStateAfterwards() => LicenseUrlRiskEnricher.ClearCache();

    [TestMethod]
    public async Task Validates_a_license_url_shared_by_several_packages_only_once()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json("{}"));
        var enricher = new LicenseUrlRiskEnricher(NullLogger.Instance, new HttpClient(handler));

        // Act
        await enricher.EnrichAsync(CreatePackage("Acme.Core"));
        await enricher.EnrichAsync(CreatePackage("Acme.Abstractions"));
        await enricher.EnrichAsync(CreatePackage("Acme.Extensions"));

        // Assert
        handler.RequestCount.Should().Be(1, "whole families of packages point at the same license");
    }

    [TestMethod]
    public async Task Asks_only_for_the_headers_of_a_license_document()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json("{}"));
        var enricher = new LicenseUrlRiskEnricher(NullLogger.Instance, new HttpClient(handler));

        // Act
        await enricher.EnrichAsync(CreatePackage("Acme.Core"));

        // Assert
        handler.Requests.Single().Method.Should().Be(HttpMethod.Head);
    }

    [TestMethod]
    public async Task Falls_back_to_a_get_when_the_server_rejects_a_head_request()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((_, attempt) => attempt == 1
            ? new HttpResponseMessage(System.Net.HttpStatusCode.MethodNotAllowed)
            : ScriptedResponse.Json("{}"));

        var enricher = new LicenseUrlRiskEnricher(NullLogger.Instance, new HttpClient(handler));
        PackageInfo package = CreatePackage("Acme.Core");

        // Act
        await enricher.EnrichAsync(package);

        // Assert
        package.HasValidLicenseUrl.Should().BeTrue();
        handler.Requests.Select(request => request.Method).Should().Equal(HttpMethod.Head, HttpMethod.Get);
    }

    [TestMethod]
    public async Task Reports_an_unreachable_license_url_as_invalid()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(ScriptedResponse.NotFound);
        var enricher = new LicenseUrlRiskEnricher(NullLogger.Instance, new HttpClient(handler));
        PackageInfo package = CreatePackage("Acme.Core");

        // Act
        await enricher.EnrichAsync(package);

        // Assert
        package.HasValidLicenseUrl.Should().BeFalse();
        package.HasValidatedLicenseUrl.Should().BeTrue();
    }

    private static PackageInfo CreatePackage(string name) =>
        new()
        {
            Name = name,
            Version = "1.0.0",
            Source = "NuGet",
            LicenseUrl = SharedLicenseUrl
        };
}
