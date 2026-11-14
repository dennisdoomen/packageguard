namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Result of fetching maintainer response data for an issue.</summary>
internal sealed class GitHubIssueResponseData
{
    /// <summary>Indicates whether a maintainer has responded to the issue.</summary>
    public bool HasMaintainerResponse { get; init; }

    /// <summary>The number of days between issue creation and the first maintainer response.</summary>
    public double? ResponseDays { get; init; }
}
