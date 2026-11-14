namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Result of checking for a CHANGELOG file.</summary>
internal sealed class GitHubChangelogData
{
    /// <summary>Indicates whether a CHANGELOG file exists in the repository root.</summary>
    public bool Exists { get; init; }

    /// <summary>Indicates whether the CHANGELOG appears to be a boilerplate default.</summary>
    public bool IsDefault { get; init; }
}
