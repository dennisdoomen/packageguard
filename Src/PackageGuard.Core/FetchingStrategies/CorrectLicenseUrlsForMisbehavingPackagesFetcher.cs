namespace PackageGuard.Core.FetchingStrategies;

/// <summary>
/// A specialized license fetcher that corrects the repository URL for packages that have historically used the wrong URL.
/// </summary>
internal class CorrectLicenseUrlsForMisbehavingPackagesFetcher : IFetchLicense
{
    public Task FetchLicenseAsync(PackageInfo package)
    {
        if (package.Name.Equals("nunit", StringComparison.InvariantCultureIgnoreCase) &&
            package.RepositoryUrl?.StartsWith("https://github.com", StringComparison.InvariantCultureIgnoreCase) == false)
        {
            package.RepositoryUrl = "https://github.com/nunit/nunit";
        }

        return Task.CompletedTask;
    }
}
