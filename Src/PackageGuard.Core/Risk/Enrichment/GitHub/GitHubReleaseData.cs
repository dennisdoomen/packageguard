namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Aggregated release metrics.</summary>
internal sealed class GitHubReleaseData
{
    /// <summary>The publication date of the most recent release.</summary>
    public DateTimeOffset? LastReleaseAt { get; init; }

    /// <summary>Indicates whether releases include substantive release notes.</summary>
    public bool? HasReleaseNotes { get; init; }

    /// <summary>Indicates whether all releases use SemVer-compatible tags.</summary>
    public bool? HasSemVerReleaseTags { get; init; }

    /// <summary>The mean number of days between consecutive releases.</summary>
    public double? MeanReleaseIntervalDays { get; init; }

    /// <summary>The fraction of consecutive release transitions that were major-version bumps.</summary>
    public double? MajorReleaseRatio { get; init; }

    /// <summary>The fraction of releases marked as pre-releases.</summary>
    public double? PrereleaseRatio { get; init; }

    /// <summary>The number of release pairs published within three days of each other, indicating rapid corrections.</summary>
    public int? RapidReleaseCorrectionCount { get; init; }

    /// <summary>Indicates whether the most recent release has a verified signature.</summary>
    public bool? HasVerifiedReleaseSignature { get; init; }
}
