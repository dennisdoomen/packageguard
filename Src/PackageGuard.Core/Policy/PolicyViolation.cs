using PackageGuard.Core.Package;

namespace PackageGuard.Core.Policy;

/// <summary>
/// Describes a package that violates the policy, including where it was found and, when known, how to resolve it.
/// </summary>
public record PolicyViolation
{
    /// <summary>
    /// Creates the violation that <paramref name="decision"/> causes for <paramref name="package"/> in
    /// <paramref name="projects"/>, taking its reason and resolution from that decision.
    /// </summary>
    internal static PolicyViolation FromDecision(PackageInfo package, string[] projects, PolicyDecision decision)
    {
        return new PolicyViolation
        {
            PackageId = package.Name,
            Version = package.Version,
            License = package.License ?? "",
            Projects = projects,
            FeedName = package.Source,
            FeedUrl = package.SourceUrl,
            Reason = decision.Reason,
            Resolution = decision.Resolution
        };
    }

    /// <summary>
    /// The identifier of the offending package.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// The version of the offending package.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The license of the package. It is "Unknown" or an empty string when the license could not be determined.
    /// </summary>
    public required string License { get; init; }

    /// <summary>
    /// The projects that reference the package.
    /// </summary>
    public required string[] Projects { get; init; }

    /// <summary>
    /// The name of the feed the package was resolved from.
    /// </summary>
    public required string FeedName { get; init; }

    /// <summary>
    /// The URL of the feed the package was resolved from.
    /// </summary>
    public required string FeedUrl { get; init; }

    /// <summary>
    /// The human-readable rule that caused the violation.
    /// </summary>
    public string Reason { get; init; } = "";

    /// <summary>
    /// Indicates that the violation is only a warning and does not fail the analysis.
    /// </summary>
    public bool IsWarning { get; init; }

    /// <summary>
    /// Indicates that the package is a known malicious package.
    /// </summary>
    public bool IsMalicious { get; init; }

    /// <summary>
    /// A hint on how to resolve the violation, or an empty string if there is none.
    /// </summary>
    public string Resolution { get; init; } = "";
}
