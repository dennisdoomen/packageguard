using PackageGuard.Core.Package;

namespace PackageGuard.Core.CSharp.FetchingStrategies;

/// <summary>
/// Recognizes license URLs that already unambiguously identify an SPDX license, so the license text
/// doesn't need to be downloaded and scanned.
/// </summary>
internal class WellKnownLicenseUrlFetcher : IFetchLicense
{
    public Task FetchLicenseAsync(PackageInfo package)
    {
        if (package.LicenseUrl is { Length: > 0 } url && WellKnownLicenseUrls.TryGetLicenseId(url, out string? licenseId))
        {
            package.License = licenseId;
            package.LicenseEvidence = LicenseEvidence.Concluded;
        }

        return Task.CompletedTask;
    }
}
