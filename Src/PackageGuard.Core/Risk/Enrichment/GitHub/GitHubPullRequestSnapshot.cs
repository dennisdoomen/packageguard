namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Lightweight snapshot of a GitHub pull request.</summary>
internal sealed class GitHubPullRequestSnapshot
{
    /// <summary>The GitHub pull request number.</summary>
    public int Number { get; init; }

    /// <summary>The author association of the pull request author (e.g. OWNER, MEMBER, CONTRIBUTOR, NONE).</summary>
    public string? AuthorAssociation { get; init; }

    /// <summary>Indicates whether the pull request was merged rather than closed unmerged.</summary>
    public bool IsMerged { get; init; }

    /// <summary>The position of the pull request in the list of most recently closed ones, starting at zero.</summary>
    public int ClosedIndex { get; init; }

    /// <summary>The number of days between opening and merging the pull request, when it was merged.</summary>
    public double? MergeDays { get; init; }
}
