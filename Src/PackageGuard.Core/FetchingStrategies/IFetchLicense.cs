using System.Threading.Tasks;

namespace PackageGuard.Core;

/// <summary>
/// Represents a strategy for fetching a license for a package.
/// </summary>
public interface IFetchLicense
{
    Task FetchLicenseAsync(PackageInfo package);
}
