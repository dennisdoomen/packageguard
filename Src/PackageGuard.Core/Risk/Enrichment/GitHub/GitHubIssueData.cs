namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Aggregated bug issue health metrics.</summary>
internal sealed class GitHubIssueData
{
    /// <summary>The number of currently open bug issues.</summary>
    public int OpenBugIssueCount { get; init; }

    /// <summary>The number of critical bug issues open for more than six months.</summary>
    public int StaleCriticalBugIssueCount { get; init; }

    /// <summary>The median number of days until a maintainer first responds to an open bug issue.</summary>
    public double? MedianIssueResponseDays { get; init; }

    /// <summary>The median number of days until a maintainer first responds to a critical open bug issue.</summary>
    public double? MedianCriticalIssueResponseDays { get; init; }

    /// <summary>The fraction of open bug issues that have received at least one maintainer response.</summary>
    public double? IssueResponseCoverage { get; init; }

    /// <summary>The median age in days of currently open bug issues.</summary>
    public double? MedianOpenBugAgeDays { get; init; }

    /// <summary>The number of bug issues closed in the last 90 days.</summary>
    public int? ClosedBugIssueCountLast90Days { get; init; }

    /// <summary>The number of bug issues reopened after being closed in the last 90 days.</summary>
    public int? ReopenedBugIssueCountLast90Days { get; init; }

    /// <summary>The fraction of open bug issues that received a maintainer response within seven days.</summary>
    public double? TriageWithinSevenDaysRate { get; init; }
}
