using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.CSharp.FetchingStrategies;
using PackageGuard.Core.Package;

namespace PackageGuard.Specs.CSharp;

[TestClass]
public class WellKnownLicenseUrlFetcherSpecs
{
    [TestMethod]
    [DataRow("https://licenses.nuget.org/MIT", "MIT")]
    [DataRow("https://licenses.nuget.org/Apache-2.0", "Apache-2.0")]
    [DataRow("http://www.apache.org/licenses/LICENSE-2.0", "Apache-2.0")]
    [DataRow("https://apache.org/licenses/LICENSE-2.0.txt", "Apache-2.0")]
    [DataRow("https://apache.org/licenses/LICENSE-2.0/", "Apache-2.0")]
    [DataRow("https://opensource.org/licenses/MIT", "MIT")]
    [DataRow("https://opensource.org/license/MIT", "MIT")]
    [DataRow("https://opensource.org/licenses/BSD-3-Clause", "BSD-3-Clause")]
    [DataRow("https://www.gnu.org/licenses/gpl-3.0.html", "GPL-3.0")]
    [DataRow("https://www.gnu.org/licenses/lgpl-2.1.txt", "LGPL-2.1")]
    [DataRow("https://unlicense.org/", "Unlicense")]
    public async Task Recognizes_well_known_license_urls_without_fetching_them(string licenseUrl, string expectedLicense)
    {
        // Arrange
        var fetcher = new WellKnownLicenseUrlFetcher();

        var package = new PackageInfo
        {
            Name = "SomePackage",
            Version = "1.0.0",
            LicenseUrl = licenseUrl
        };

        // Act
        await fetcher.FetchLicenseAsync(package);

        // Assert
        package.License.Should().Be(expectedLicense);
        package.LicenseEvidence.Should().Be(LicenseEvidence.Concluded);
    }

    [TestMethod]
    public async Task Leaves_the_license_unset_for_an_unrecognized_url()
    {
        // Arrange
        var fetcher = new WellKnownLicenseUrlFetcher();

        var package = new PackageInfo
        {
            Name = "SomePackage",
            Version = "1.0.0",
            LicenseUrl = "https://raw.githubusercontent.com/some/repo/main/LICENSE"
        };

        // Act
        await fetcher.FetchLicenseAsync(package);

        // Assert
        package.License.Should().BeNull();
    }

    [TestMethod]
    public async Task Does_nothing_when_there_is_no_license_url()
    {
        // Arrange
        var fetcher = new WellKnownLicenseUrlFetcher();

        var package = new PackageInfo
        {
            Name = "SomePackage",
            Version = "1.0.0",
            LicenseUrl = null
        };

        // Act
        await fetcher.FetchLicenseAsync(package);

        // Assert
        package.License.Should().BeNull();
    }
}
