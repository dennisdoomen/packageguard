using MemoryPack;

namespace PackageGuard.Core.GitHub;

/// <summary>Aggregated repository risk metrics for a GitHub repository.</summary>
[MemoryPackable]
internal sealed partial class GitHubRepositoryRiskData
{
    /// <summary>The canonical HTML URL of the repository as returned by the GitHub API.</summary>
    public string CanonicalUrl { get; init; } = string.Empty;

    /// <summary>Indicates whether the repository owner is an organization.</summary>
    public bool OwnerIsOrganization { get; init; }

    /// <summary>The date the repository owner account was created.</summary>
    public DateTimeOffset? OwnerCreatedAt { get; init; }

    /// <summary>The total number of non-bot contributors.</summary>
    public int ContributorCount { get; init; }

    /// <summary>The fraction of total contributions made by the top contributor.</summary>
    public double? TopContributorShare { get; init; }

    /// <summary>The fraction of total contributions made by the top two contributors combined.</summary>
    public double? TopTwoContributorShare { get; init; }

    /// <summary>The number of maintainers with commit activity in the last six months.</summary>
    public int? RecentMaintainerCount { get; init; }

    /// <summary>Indicates whether the repository has a README file.</summary>
    public bool HasReadme { get; init; }

    /// <summary>Indicates whether the README appears to be a boilerplate default.</summary>
    public bool HasDefaultReadme { get; init; }

    /// <summary>The date of the most recent commit that touched the README file.</summary>
    public DateTimeOffset? ReadmeUpdatedAt { get; init; }

    /// <summary>Indicates whether the repository has a CONTRIBUTING.md file.</summary>
    public bool HasContributingGuide { get; init; }

    /// <summary>Indicates whether the repository has a SECURITY.md file.</summary>
    public bool HasSecurityPolicy { get; init; }

    /// <summary>Indicates whether the security policy contains detailed contact and reporting information.</summary>
    public bool? HasDetailedSecurityPolicy { get; init; }

    /// <summary>Indicates whether the security policy describes a coordinated disclosure process.</summary>
    public bool? HasCoordinatedDisclosure { get; init; }

    /// <summary>Indicates whether the repository has a CHANGELOG file.</summary>
    public bool HasChangelog { get; init; }

    /// <summary>Indicates whether the CHANGELOG appears to be a boilerplate default.</summary>
    public bool HasDefaultChangelog { get; init; }

    /// <summary>The date of the most recent commit that touched the CHANGELOG file.</summary>
    public DateTimeOffset? ChangelogUpdatedAt { get; init; }

    /// <summary>The number of currently open bug issues.</summary>
    public int OpenBugIssueCount { get; init; }

    /// <summary>The number of critical bug issues that have been open for more than six months.</summary>
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

    /// <summary>The number of bug issues that were reopened after being closed in the last 90 days.</summary>
    public int? ReopenedBugIssueCountLast90Days { get; init; }

    /// <summary>The fraction of open bug issues that received a maintainer response within seven days.</summary>
    public double? IssueTriageWithinSevenDaysRate { get; init; }

    /// <summary>The median number of days from pull request creation to merge.</summary>
    public double? MedianPullRequestMergeDays { get; init; }

    /// <summary>The fraction of recently merged pull requests authored by external contributors.</summary>
    public double? ExternalContributionRate { get; init; }

    /// <summary>The number of unique reviewers across recently merged pull requests.</summary>
    public int? UniqueReviewerCount { get; init; }

    /// <summary>The ratio of unique reviewers to total recently merged pull requests.</summary>
    public double? ReviewerDiversityRatio { get; init; }

    /// <summary>The number of failed workflow runs among the most recent runs on the default branch.</summary>
    public int? RecentFailedWorkflowCount { get; init; }

    /// <summary>Indicates whether there is at least one recent successful workflow run on the default branch.</summary>
    public bool? HasRecentSuccessfulWorkflowRun { get; init; }

    /// <summary>The fraction of completed workflow runs that failed.</summary>
    public double? WorkflowFailureRate { get; init; }

    /// <summary>Indicates whether the workflow runs show a flaky pattern (intermittent failures and successes).</summary>
    public bool? HasFlakyWorkflowPattern { get; init; }

    /// <summary>The number of required status checks configured on the default branch.</summary>
    public int? RequiredStatusCheckCount { get; init; }

    /// <summary>The number of distinct operating system platforms targeted by workflow files.</summary>
    public int? WorkflowPlatformCount { get; init; }

    /// <summary>Indicates whether workflow files contain a coverage reporting signal.</summary>
    public bool? HasCoverageWorkflowSignal { get; init; }

    /// <summary>Indicates whether workflow files or Scorecard results suggest reproducible builds.</summary>
    public bool? HasReproducibleBuildSignal { get; init; }

    /// <summary>Indicates whether the repository uses automated dependency update tooling.</summary>
    public bool? HasDependencyUpdateAutomation { get; init; }

    /// <summary>Indicates whether workflow files contain a test execution signal.</summary>
    public bool? HasTestSignal { get; init; }

    /// <summary>The OpenSSF Scorecard aggregate score for the repository.</summary>
    public double? OpenSsfScore { get; init; }

    /// <summary>Indicates whether the default branch has branch protection enabled.</summary>
    public bool? HasBranchProtection { get; init; }

    /// <summary>Indicates whether workflow files contain a provenance attestation signal.</summary>
    public bool? HasProvenanceAttestation { get; init; }

    /// <summary>Indicates whether the most recent release has a verified signature.</summary>
    public bool? HasVerifiedReleaseSignature { get; init; }

    /// <summary>Indicates whether the repository owner is an organization, used as a proxy for a verified publisher.</summary>
    public bool? HasVerifiedPublisher { get; init; }

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

    /// <summary>The fraction of sampled commits that have a verified signature.</summary>
    public double? VerifiedCommitRatio { get; init; }

    /// <summary>The median number of days since the last commit activity for each active maintainer.</summary>
    public double? MedianMaintainerActivityDays { get; init; }

    /// <summary>The publication date of the most recent release.</summary>
    public DateTimeOffset? LastReleaseAt { get; init; }
}
