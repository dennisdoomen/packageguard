namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>The issue responsiveness metrics measured over a sample of the open bug issues.</summary>
internal sealed class GitHubIssueResponsiveness
{
    /// <summary>The median number of days before a maintainer first responded.</summary>
    public double? MedianResponseDays { get; init; }

    /// <summary>The median number of days before a maintainer first responded to a critical issue.</summary>
    public double? MedianCriticalResponseDays { get; init; }

    /// <summary>The fraction of sampled issues that received a maintainer response.</summary>
    public double? ResponseCoverage { get; init; }

    /// <summary>The fraction of sampled issues that received a response within seven days.</summary>
    public double? TriageWithinSevenDaysRate { get; init; }
}
