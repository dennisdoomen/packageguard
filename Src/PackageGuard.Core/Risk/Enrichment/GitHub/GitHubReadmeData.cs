namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Result of checking for a README file.</summary>
internal sealed class GitHubReadmeData
{
    /// <summary>Indicates whether a README file exists in the repository.</summary>
    public bool Exists { get; init; }

    /// <summary>Indicates whether the README appears to be a boilerplate default.</summary>
    public bool IsDefault { get; init; }
}
