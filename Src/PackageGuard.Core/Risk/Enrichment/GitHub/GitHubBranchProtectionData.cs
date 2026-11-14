namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Branch protection settings for the default branch.</summary>
internal sealed class GitHubBranchProtectionData
{
    /// <summary>Indicates whether the default branch has protection rules enabled.</summary>
    public bool? IsProtected { get; init; }

    /// <summary>The number of required status checks configured on the default branch.</summary>
    public int? RequiredStatusCheckCount { get; init; }
}
