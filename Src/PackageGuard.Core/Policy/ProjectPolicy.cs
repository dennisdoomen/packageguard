using PackageGuard.Core.Package;

namespace PackageGuard.Core.Policy;

/// <summary>
/// Represents the configuration policies applicable to a project, including
/// allowed packages, licenses and feeds, and denied packages and licenses.
/// </summary>
public class ProjectPolicy
{
    /// <summary>
    /// If specified, a list of packages, versions, and licenses that are allowed. Everything else is forbidden.
    /// </summary>
    /// <remarks>
    /// Can be overridden by <see cref="DenyList"/>
    /// </remarks>
    public AllowList AllowList { get; set; } = new();

    /// <summary>
    /// If specified, a list of packages, versions, and licenses that are forbidden, even if it was listed in <see cref="AllowList"/>.
    /// </summary>
    public DenyList DenyList { get; set; } = new();

    /// <summary>
    /// If specified, a list of packages and licenses that log a warning instead of failing the build.
    /// A match in <see cref="DenyList"/> always takes precedence over a match here.
    /// </summary>
    public WarnList WarnList { get; init; } = new();

    /// <summary>
    /// Explicit exceptions that keep specific packages (optionally pinned to a version range) from being
    /// denied by <see cref="DenyList"/>'s risk-based rules, even if they exceed a threshold.
    /// </summary>
    public List<RiskException> RiskExceptions { get; init; } = new();

    /// <summary>
    /// One or more NuGet or NPM feeds that should be completely ignored during the analysis.
    /// </summary>
    /// <value>
    /// Each feed is wildcard string that can match the NPM or NuGet feed name or URL.
    /// </value>
    public string[] IgnoredFeeds { get; set; } = [];

    /// <summary>
    /// Validates the current project policy to ensure that at least one policy
    /// (allowlist, denylist, or warnlist) is specified. Throws an exception if no policies
    /// are defined.
    /// </summary>
    public void Validate()
    {
        if (this is
            {
                AllowList.HasPolicies: false,
                DenyList: { HasPolicies: false, HasRiskPolicies: false },
                WarnList.HasPolicies: false
            })
        {
            throw new ArgumentException("Either a allowlist or a denylist must be specified");
        }
    }

    /// <summary>
    /// Returns the <see cref="RiskExceptions"/> entry that currently applies to <paramref name="package"/>,
    /// exempting it from <see cref="DenyList"/>'s risk-based rules, or <see langword="null"/> when none applies.
    /// </summary>
    internal RiskException? FindApplicableRiskException(PackageInfo package) =>
        RiskExceptions.FirstOrDefault(exception => exception.Matches(package));

    /// <summary>
    /// Determines whether <paramref name="package"/> is covered by a <see cref="RiskExceptions"/> entry that
    /// currently applies, exempting it from <see cref="DenyList"/>'s risk-based rules.
    /// </summary>
    internal bool IsExcludedFromRiskDenial(PackageInfo package) => FindApplicableRiskException(package) is not null;
}
