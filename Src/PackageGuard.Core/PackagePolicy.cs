namespace PackageGuard.Core;

public abstract class PackagePolicy
{
    public List<PackageSelector> Packages { get; set; } = new();

    /// <summary>
    /// A list of licenses using SPDX format.
    /// </summary>
    public List<string> Licenses { get; set; } = new();

    /// <summary>
    /// Checks if there are any policies defined either in Packages or Licenses.
    /// </summary>
    internal bool HasPolicies => Packages.Any() || Licenses.Any();

    internal abstract bool Complies(PackageInfo package);
}
