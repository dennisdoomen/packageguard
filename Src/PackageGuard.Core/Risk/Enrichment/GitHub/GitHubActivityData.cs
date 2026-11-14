namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>The issue and pull request metrics of a repository, however they were collected.</summary>
/// <param name="IssueData">The issue health metrics.</param>
/// <param name="PullRequestQualityData">The pull request merge-time and review metrics.</param>
internal sealed record GitHubActivityData(
    GitHubIssueData IssueData,
    GitHubPullRequestQualityData PullRequestQualityData);
