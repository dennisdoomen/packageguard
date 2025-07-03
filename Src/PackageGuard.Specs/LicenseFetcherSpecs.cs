using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
public class LicenseFetcherSpecs
{
    private readonly string gitHubApiKey = Environment.GetEnvironmentVariable("GITHUB_API_KEY");

    [TestMethod]
    public async Task Nothing_needs_to_be_done_for_a_package_that_already_has_a_license()
    {
        // Arrange
        var fetcher = new LicenseFetcher(NullLogger.Instance, gitHubApiKey);

        var package = new PackageInfo
        {
            Name = "Bogus",
            Version = "1.0.0",
            License = "MIT"
        };

        // Act / Assert
        await fetcher.AmendWithMissingLicenseInformation(package);
    }

    [TestMethod]
    public async Task A_package_without_license_or_license_url_is_properly_handled()
    {
        // Arrange
        var fetcher = new LicenseFetcher(NullLogger.Instance, gitHubApiKey);

        var package = new PackageInfo
        {
            Name = "Bogus",
            Version = "1.0.0",
            License = null,
            LicenseUrl = null
        };

        // Act
        await fetcher.AmendWithMissingLicenseInformation(package);

        // Assert
        package.License.Should().Be("Unknown");
    }

    [TestMethod]
    public async Task A_no_assertion_license_is_treated_as_unknown()
    {
        // Arrange
        var fetcher = new LicenseFetcher(NullLogger.Instance, gitHubApiKey);

        var package = new PackageInfo
        {
            Name = "xunit.abstractions",
            Version = "2.0.3",
            RepositoryUrl = "https://github.com/xunit/xunit"
        };

        // Act
        await fetcher.AmendWithMissingLicenseInformation(package);

        // Assert
        package.License.Should().Be("Unknown");
    }
 }
