namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>The pending collections of the signals that only need the repository itself, all started at once.</summary>
internal sealed class GitHubRepositoryWideSignals
{
    /// <summary>The release cadence, versioning and signing metrics.</summary>
    public required Task<GitHubReleaseData> Releases { get; init; }

    /// <summary>Whether the repository has a README and whether it is boilerplate.</summary>
    public required Task<Tracked<GitHubReadmeData>> Readme { get; init; }

    /// <summary>The names of the files and directories in the repository root.</summary>
    public required Task<Tracked<string[]>> RootFiles { get; init; }

    /// <summary>The issue and pull request metrics.</summary>
    public required Task<GitHubActivityData> Activity { get; init; }

    /// <summary>The contributor count and concentration.</summary>
    public required Task<Tracked<GitHubContributorData>> Contributors { get; init; }

    /// <summary>The maintainer activity and commit verification metrics.</summary>
    public required Task<GitHubCommitHealthData> CommitHealth { get; init; }

    /// <summary>The outcome of the recent CI workflow runs.</summary>
    public required Task<GitHubWorkflowData> WorkflowRuns { get; init; }

    /// <summary>The protection rules of the default branch.</summary>
    public required Task<GitHubBranchProtectionData> BranchProtection { get; init; }

    /// <summary>The OpenSSF Scorecard results.</summary>
    public required Task<GitHubScorecardData> Scorecard { get; init; }

    /// <summary>Completes when every collection has finished.</summary>
    public Task WhenAllAsync() =>
        Task.WhenAll(Releases, Readme, RootFiles, Activity, Contributors, CommitHealth, WorkflowRuns, BranchProtection,
            Scorecard);
}
