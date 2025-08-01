namespace PackageGuard.Core;

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
    /// One or more NuGet feeds that should be completely ignored during the analysis.
    /// </summary>
    /// <value>
    /// Each feed is wildcard string that can match the NuGet feed name or URL.
    /// </value>
    public string[] IgnoredFeeds { get; set; } = [];

    /// <summary>
    /// Validates the current project policy to ensure that at least one policy
    /// (allowlist or denylist) is specified. Throws an exception if no policies
    /// are defined.
    /// </summary>
    public void Validate()
    {
        if (!AllowList.HasPolicies && !DenyList.HasPolicies)
        {
            throw new ArgumentException("Either a allowlist or a denylist must be specified");
        }
    }
}
