namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Result of analyzing the SECURITY.md file.</summary>
internal sealed class GitHubSecurityPolicyData
{
    /// <summary>Indicates whether a SECURITY.md file exists in the repository.</summary>
    public bool Exists { get; init; }

    /// <summary>Indicates whether the security policy contains detailed contact and reporting information.</summary>
    public bool? IsDetailed { get; init; }

    /// <summary>Indicates whether the security policy describes a coordinated disclosure process.</summary>
    public bool? HasCoordinatedDisclosure { get; init; }
}
