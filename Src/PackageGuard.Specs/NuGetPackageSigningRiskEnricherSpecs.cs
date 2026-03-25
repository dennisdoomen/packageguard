using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core;

namespace PackageGuard.Specs;

[TestClass]
public class NuGetPackageSigningRiskEnricherSpecs
{
    private string testDirectory = null!;

    [TestInitialize]
    public void SetUp()
    {
        testDirectory = Path.Combine(Path.GetTempPath(), "PackageGuard-SigningSpecs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, true);
        }
    }

    [TestMethod]
    public async Task Should_mark_package_as_signed_when_signature_file_exists()
    {
        string packagePath = CreatePackageArchive("Test.Package", "1.0.0", signed: true);
        var enricher = new NuGetPackageSigningRiskEnricher(NullLogger.Instance, testDirectory);
        var package = new PackageInfo { Name = "Test.Package", Version = "1.0.0", Source = "nuget" };

        await enricher.EnrichAsync(package);

        package.IsPackageSigned.Should().BeTrue();
        File.Exists(packagePath).Should().BeTrue();
    }

    [TestMethod]
    public async Task Should_mark_package_as_unsigned_when_signature_file_is_missing()
    {
        var enricher = new NuGetPackageSigningRiskEnricher(NullLogger.Instance, testDirectory);
        var package = new PackageInfo { Name = "Test.Package", Version = "1.0.0", Source = "nuget" };

        CreatePackageArchive("Test.Package", "1.0.0", signed: false);
        await enricher.EnrichAsync(package);

        package.IsPackageSigned.Should().BeFalse();
    }

    [TestMethod]
    public async Task Should_leave_signing_status_unknown_when_package_archive_is_missing()
    {
        var enricher = new NuGetPackageSigningRiskEnricher(NullLogger.Instance, testDirectory);
        var package = new PackageInfo { Name = "Missing.Package", Version = "1.0.0", Source = "nuget" };

        await enricher.EnrichAsync(package);

        package.IsPackageSigned.Should().BeNull();
    }

    [TestMethod]
    public async Task Should_detect_native_binary_assets()
    {
        var enricher = new NuGetPackageSigningRiskEnricher(NullLogger.Instance, testDirectory);
        var package = new PackageInfo { Name = "Native.Package", Version = "1.0.0", Source = "nuget" };

        CreatePackageArchive("Native.Package", "1.0.0", signed: false, includeNativeBinary: true);
        await enricher.EnrichAsync(package);

        package.HasNativeBinaryAssets.Should().BeTrue();
    }

    private string CreatePackageArchive(string packageId, string version, bool signed, bool includeNativeBinary = false)
    {
        string folder = Path.Combine(testDirectory, packageId.ToLowerInvariant(), version.ToLowerInvariant());
        Directory.CreateDirectory(folder);

        string packagePath = Path.Combine(folder, $"{packageId.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg");
        using ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        archive.CreateEntry("lib/net9.0/_._");

        if (includeNativeBinary)
        {
            archive.CreateEntry("runtimes/win-x64/native/native.dll");
        }

        if (signed)
        {
            archive.CreateEntry(".signature.p7s");
        }

        return packagePath;
    }
}
