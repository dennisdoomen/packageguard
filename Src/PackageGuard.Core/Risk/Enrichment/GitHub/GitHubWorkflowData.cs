namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Aggregated CI workflow run statistics.</summary>
internal sealed class GitHubWorkflowData
{
    /// <summary>The number of failed workflow runs among the most recent runs on the default branch.</summary>
    public int? RecentFailedWorkflowCount { get; init; }

    /// <summary>Indicates whether there is at least one recent successful workflow run on the default branch.</summary>
    public bool? HasRecentSuccessfulWorkflowRun { get; init; }

    /// <summary>The fraction of completed workflow runs that failed.</summary>
    public double? FailureRate { get; init; }

    /// <summary>Indicates whether the workflow runs show a flaky pattern (intermittent failures and successes).</summary>
    public bool? HasFlakyPattern { get; init; }
}
