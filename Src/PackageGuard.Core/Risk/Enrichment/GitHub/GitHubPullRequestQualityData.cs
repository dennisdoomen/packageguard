namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Aggregated pull request quality metrics.</summary>
internal sealed class GitHubPullRequestQualityData
{
    /// <summary>The fraction of recently merged pull requests authored by external contributors.</summary>
    public double? ExternalContributionRate { get; init; }

    /// <summary>The number of unique reviewers across recently merged pull requests.</summary>
    public int? UniqueReviewerCount { get; init; }

    /// <summary>The ratio of unique reviewers to total recently merged pull requests.</summary>
    public double? ReviewerDiversityRatio { get; init; }

    /// <summary>The median number of days between opening and merging a pull request.</summary>
    public double? MedianMergeDays { get; init; }
}
