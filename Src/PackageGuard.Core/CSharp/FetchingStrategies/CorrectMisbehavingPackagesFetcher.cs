namespace PackageGuard.Core.CSharp.FetchingStrategies;

/// <summary>
/// A specialized license fetcher that corrects the metadata for packages that have historically used the wrong repository URL or missed license information.
/// </summary>
internal class CorrectMisbehavingPackagesFetcher : IFetchLicense
{
    /// <summary>
    /// Applies known corrections to the repository URL and license information for packages
    /// that have historically provided incorrect or missing metadata.
    /// </summary>
    /// <param name="package">The package whose metadata should be corrected.</param>
    /// <returns>A completed task.</returns>
    public Task FetchLicenseAsync(PackageInfo package)
    {
        if (package.Name.Equals("nunit", StringComparison.InvariantCultureIgnoreCase) &&
            package.RepositoryUrl?.StartsWith("https://github.com", StringComparison.InvariantCultureIgnoreCase) != true)
        {
            package.RepositoryUrl = "https://github.com/nunit/nunit";
        }

        if (package.Name.Equals("NETStandard.Library", StringComparison.InvariantCultureIgnoreCase))
        {
            package.License = "MIT";
            package.RepositoryUrl = "https://github.com/dotnet/standard";
        }

        return Task.CompletedTask;
    }
}
