namespace PackageGuard.Core.Policy;

public abstract class PackagePolicy
{
    public List<PackageSelector> Packages { get; set; } = new();

    /// <summary>
    /// A list of licenses using SPDX format.
    /// </summary>
    public List<string> Licenses { get; set; } = new();

    /// <summary>
    /// Gets or sets the configuration file each entry in <see cref="Licenses"/> was first introduced by,
    /// keyed by the license string. Populated by <c>ConfigurationLoader</c>; empty when unknown.
    /// </summary>
    public Dictionary<string, string> LicenseSourceFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if there are any policies defined either in Packages or Licenses.
    /// </summary>
    internal bool HasPolicies => Packages.Any() || Licenses.Any();
}
