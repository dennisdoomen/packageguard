namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Aggregated contributor statistics.</summary>
internal sealed class GitHubContributorData
{
    /// <summary>The total number of non-bot contributors.</summary>
    public int ContributorCount { get; init; }

    /// <summary>The fraction of total contributions made by the top contributor.</summary>
    public double? TopContributorShare { get; init; }

    /// <summary>The fraction of total contributions made by the top two contributors combined.</summary>
    public double? TopTwoContributorShare { get; init; }
}
