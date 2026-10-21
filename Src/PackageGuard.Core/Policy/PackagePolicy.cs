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

    /// <summary>
    /// Names the configuration file a rule came from in a resolution hint. Configuration files are hierarchical, so
    /// the file is named when it is known, and the hint falls back to "your config" when it is not.
    /// </summary>
    private protected static string DescribeConfigurationFile(string? sourceFile) => sourceFile ?? "your config";

    /// <summary>
    /// Finds the configuration files that introduced <paramref name="licenses"/>, separated by commas, or
    /// <see langword="null"/> if none of them is known.
    /// </summary>
    private protected string? FindLicenseSourceFiles(IEnumerable<string> licenses)
    {
        string[] sourceFiles = licenses
            .Select(license => LicenseSourceFiles.GetValueOrDefault(license))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return sourceFiles.Length == 0 ? null : string.Join(", ", sourceFiles);
    }
}
