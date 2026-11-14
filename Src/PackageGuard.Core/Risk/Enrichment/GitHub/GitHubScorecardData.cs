namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>OpenSSF Scorecard data for the repository.</summary>
internal sealed class GitHubScorecardData
{
    /// <summary>The aggregate OpenSSF Scorecard score.</summary>
    public double? Score { get; init; }

    /// <summary>Indicates whether the Scorecard Branch-Protection check reported a positive score.</summary>
    public bool? HasBranchProtection { get; init; }

    /// <summary>The Scorecard Binary-Artifacts check score.</summary>
    public double? BinaryArtifactsScore { get; init; }
}
