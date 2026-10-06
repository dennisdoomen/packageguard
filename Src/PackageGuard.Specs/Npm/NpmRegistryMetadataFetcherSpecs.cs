using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Npm;
using PackageGuard.Core.Package;
using PackageGuard.Specs.Common;

namespace PackageGuard.Specs.Npm;

[TestClass]
[DoNotParallelize]
public class NpmRegistryMetadataFetcherSpecs
{
    [TestInitialize]
    public void ClearSharedState() => NpmRegistryMetadataFetcher.ClearPackumentCache();

    [TestCleanup]
    public void ClearSharedStateAfterwards() => NpmRegistryMetadataFetcher.ClearPackumentCache();

    [TestMethod]
    public async Task Downloads_the_registry_document_of_a_package_only_once()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json(Packument));
        var fetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance, new HttpClient(handler));

        // Act
        await fetcher.FetchMetadataAsync(CreatePackage("4.17.15"));
        await fetcher.FetchMetadataAsync(CreatePackage("4.17.21"));

        // Assert
        handler.RequestCount.Should().Be(1,
            "a package referenced at two versions describes both from one registry document");
    }

    [TestMethod]
    public async Task Reads_the_version_specific_metadata_of_each_referenced_version()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json(Packument));
        var fetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance, new HttpClient(handler));
        PackageInfo older = CreatePackage("4.17.15");
        PackageInfo newer = CreatePackage("4.17.21");

        // Act
        await fetcher.FetchMetadataAsync(older);
        await fetcher.FetchMetadataAsync(newer);

        // Assert
        older.PublishedAt.Should().Be(new System.DateTimeOffset(2019, 7, 19, 0, 0, 0, System.TimeSpan.Zero));
        older.IsMinorVersionBehindLatest.Should().BeTrue("4.17.21 is the latest version");
        newer.PublishedAt.Should().Be(new System.DateTimeOffset(2021, 2, 20, 0, 0, 0, System.TimeSpan.Zero));
        newer.IsMinorVersionBehindLatest.Should().BeFalse("this is the latest version");
    }

    [TestMethod]
    public async Task Reads_a_license_that_the_registry_describes_as_an_object()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json(
            """{"name":"legacy","license":{"type":"MIT","url":"https://example.com/license"},"dist-tags":{"latest":"1.0.0"},"versions":{"1.0.0":{"license":{"type":"MIT","url":"https://example.com/license"}}}}"""));

        var fetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance, new HttpClient(handler));
        PackageInfo package = CreatePackage("1.0.0", "legacy");

        // Act
        await fetcher.FetchMetadataAsync(package);

        // Assert
        package.License.Should().Be("MIT");
    }

    [TestMethod]
    public async Task Builds_a_browsable_license_url_from_an_ssh_repository()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json(
            """{"name":"source-map","license":"BSD-3-Clause","repository":{"type":"git","url":"git+ssh://git@github.com/mozilla/source-map.git"},"dist-tags":{"latest":"0.6.1"},"versions":{"0.6.1":{}}}"""));

        var fetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance, new HttpClient(handler));
        PackageInfo package = CreatePackage("0.6.1", "source-map");

        // Act
        await fetcher.FetchMetadataAsync(package);

        // Assert
        package.RepositoryUrl.Should().Be("https://github.com/mozilla/source-map");
        package.LicenseUrl.Should().Be("https://github.com/mozilla/source-map/blob/master/LICENSE");
    }

    [TestMethod]
    public async Task Resolves_the_download_counts_of_several_packages_in_one_request()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.Host == "api.npmjs.org"
                ? ScriptedResponse.Json("""{"lodash":{"downloads":42},"express":{"downloads":7}}""")
                : ScriptedResponse.Json(Packument));

        var fetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance, new HttpClient(handler));
        PackageInfo lodash = CreatePackage("4.17.21");
        PackageInfo express = CreatePackage("4.17.21", "express");

        await fetcher.FetchMetadataAsync(lodash);
        await fetcher.FetchMetadataAsync(express);

        // Act
        await fetcher.ResolveDownloadCountsAsync();

        // Assert
        handler.RequestedUrls.Count(url => url.Contains("api.npmjs.org")).Should().Be(1,
            "the downloads API takes a list of names");

        lodash.DownloadCount.Should().Be(42);
        express.DownloadCount.Should().Be(7);
    }

    [TestMethod]
    public async Task Asks_for_a_scoped_package_on_its_own()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.Host == "api.npmjs.org"
                ? ScriptedResponse.Json("""{"downloads":11}""")
                : ScriptedResponse.Json(Packument));

        var fetcher = new NpmRegistryMetadataFetcher(NullLogger.Instance, new HttpClient(handler));
        PackageInfo scoped = CreatePackage("4.17.21", "@acme/widget");
        await fetcher.FetchMetadataAsync(scoped);

        // Act
        await fetcher.ResolveDownloadCountsAsync();

        // Assert
        scoped.DownloadCount.Should().Be(11, "bulk lookups reject scoped names");
    }

    private static PackageInfo CreatePackage(string version, string name = "lodash") =>
        new()
        {
            Name = name,
            Version = version,
            Source = "npm",
            SourceUrl = "https://registry.npmjs.org"
        };

    private const string Packument =
        """
        {
          "name": "lodash",
          "dist-tags": { "latest": "4.17.21" },
          "time": {
            "4.17.15": "2019-07-19T00:00:00.000Z",
            "4.17.21": "2021-02-20T00:00:00.000Z"
          },
          "license": "MIT",
          "repository": { "url": "git+https://github.com/lodash/lodash.git" },
          "versions": {
            "4.17.15": { "license": "MIT" },
            "4.17.21": { "license": "MIT" }
          }
        }
        """;
}
