using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Combines the collected GitHub signals into the risk profile of a repository.</summary>
internal static class GitHubRepositoryRiskDataAssembler
{
    /// <summary>
    /// Builds the risk profile from collections that have all finished. A signal is left unknown when a request
    /// behind it got no answer, so that it is not mistaken for an absent file or setting.
    /// </summary>
    public static async Task<GitHubRepositoryRiskData> AssembleAsync(GitHubRepositoryContext repository,
        GitHubRepositoryWideSignals repositoryWide, GitHubRootFileSignals rootFileBased)
    {
        GitHubReleaseData releaseData = await repositoryWide.Releases;
        Tracked<GitHubReadmeData> readmeData = await repositoryWide.Readme;
        Tracked<string[]> trackedRootFiles = await repositoryWide.RootFiles;
        GitHubActivityData activityData = await repositoryWide.Activity;
        GitHubIssueData issueData = activityData.IssueData;
        Tracked<GitHubContributorData> contributorData = await repositoryWide.Contributors;
        GitHubCommitHealthData commitHealthData = await repositoryWide.CommitHealth;
        GitHubPullRequestQualityData pullRequestQualityData = activityData.PullRequestQualityData;
        GitHubWorkflowData workflowData = await repositoryWide.WorkflowRuns;
        GitHubBranchProtectionData branchProtectionData = await repositoryWide.BranchProtection;
        Tracked<GitHubChangelogData> changelogData = await rootFileBased.Changelog;
        Tracked<GitHubSecurityPolicyData> securityPolicyData = await rootFileBased.SecurityPolicy;
        Tracked<GitHubWorkflowFileSignals> trackedWorkflowFileSignals = await rootFileBased.WorkflowFiles;
        GitHubScorecardData scorecardData = await repositoryWide.Scorecard;

        // A file found through the root listing is only known when both the listing and the file could be read.
        bool rootFilesKnown = trackedRootFiles.IsComplete;
        string[] rootFiles = trackedRootFiles.Value;
        GitHubReadmeData? readme = readmeData.KnownValue;
        GitHubContributorData? contributors = contributorData.KnownValue;
        // The listing alone shows whether a changelog exists, but telling whether it is a default one needs its content.
        GitHubChangelogData? changelog = rootFilesKnown ? changelogData.KnownValue : null;
        GitHubSecurityPolicyData? securityPolicy = rootFilesKnown ? securityPolicyData.KnownValue : null;
        GitHubWorkflowFileSignals? workflowFileSignals = rootFilesKnown ? trackedWorkflowFileSignals.KnownValue : null;

        return new GitHubRepositoryRiskData
        {
            CanonicalUrl = repository.CanonicalUrl,
            OwnerIsOrganization = repository.OwnerIsOrganization,
            OwnerCreatedAt = repository.OwnerCreatedAt,
            ContributorCount = contributors?.ContributorCount,
            TopContributorShare = contributorData.Value.TopContributorShare,
            TopTwoContributorShare = contributorData.Value.TopTwoContributorShare,
            RecentMaintainerCount = commitHealthData.RecentMaintainerCount,
            HasReadme = readme?.Exists,
            HasDefaultReadme = readme?.IsDefault,
            ReadmeUpdatedAt = await rootFileBased.ReadmeUpdatedAt,
            HasContributingGuide =
                rootFilesKnown ? rootFiles.Contains("CONTRIBUTING.md", StringComparer.OrdinalIgnoreCase) : null,
            HasSecurityPolicy = securityPolicy?.Exists,
            HasDetailedSecurityPolicy = securityPolicy?.IsDetailed,
            HasCoordinatedDisclosure = securityPolicy?.HasCoordinatedDisclosure,
            HasChangelog = rootFilesKnown ? changelogData.Value.Exists : null,
            HasDefaultChangelog = changelog?.IsDefault,
            ChangelogUpdatedAt = await rootFileBased.ChangelogUpdatedAt,
            OpenBugIssueCount = issueData.OpenBugIssueCount,
            StaleCriticalBugIssueCount = issueData.StaleCriticalBugIssueCount,
            MedianIssueResponseDays = issueData.MedianIssueResponseDays,
            MedianCriticalIssueResponseDays = issueData.MedianCriticalIssueResponseDays,
            IssueResponseCoverage = issueData.IssueResponseCoverage,
            MedianOpenBugAgeDays = issueData.MedianOpenBugAgeDays,
            ClosedBugIssueCountLast90Days = issueData.ClosedBugIssueCountLast90Days,
            ReopenedBugIssueCountLast90Days = issueData.ReopenedBugIssueCountLast90Days,
            IssueTriageWithinSevenDaysRate = issueData.TriageWithinSevenDaysRate,
            MedianPullRequestMergeDays = pullRequestQualityData.MedianMergeDays,
            ExternalContributionRate = pullRequestQualityData.ExternalContributionRate,
            UniqueReviewerCount = pullRequestQualityData.UniqueReviewerCount,
            ReviewerDiversityRatio = pullRequestQualityData.ReviewerDiversityRatio,
            RecentFailedWorkflowCount = workflowData.RecentFailedWorkflowCount,
            HasRecentSuccessfulWorkflowRun = workflowData.HasRecentSuccessfulWorkflowRun,
            WorkflowFailureRate = workflowData.FailureRate,
            HasFlakyWorkflowPattern = workflowData.HasFlakyPattern,
            RequiredStatusCheckCount = branchProtectionData.RequiredStatusCheckCount,
            WorkflowPlatformCount = workflowFileSignals?.PlatformCount,
            HasCoverageWorkflowSignal = workflowFileSignals?.HasCoverageSignal,
            HasReproducibleBuildSignal =
                scorecardData.BinaryArtifactsScore >= 8.0 ? true : workflowFileSignals?.HasReproducibleBuildSignal,
            HasDependencyUpdateAutomation = workflowFileSignals?.HasDependencyUpdateAutomation,
            HasTestSignal = workflowFileSignals?.HasTestSignal,
            OpenSsfScore = scorecardData.Score,
            HasBranchProtection = branchProtectionData.IsProtected ?? scorecardData.HasBranchProtection,
            HasProvenanceAttestation = workflowFileSignals?.HasProvenanceAttestation,
            HasVerifiedReleaseSignature = releaseData.HasVerifiedReleaseSignature,
            HasVerifiedPublisher = repository.OwnerIsOrganization,
            HasReleaseNotes = releaseData.HasReleaseNotes,
            HasSemVerReleaseTags = releaseData.HasSemVerReleaseTags,
            MeanReleaseIntervalDays = releaseData.MeanReleaseIntervalDays,
            MajorReleaseRatio = releaseData.MajorReleaseRatio,
            PrereleaseRatio = releaseData.PrereleaseRatio,
            RapidReleaseCorrectionCount = releaseData.RapidReleaseCorrectionCount,
            VerifiedCommitRatio = commitHealthData.VerifiedCommitRatio,
            MedianMaintainerActivityDays = commitHealthData.MedianMaintainerActivityDays,
            LastReleaseAt = releaseData.LastReleaseAt
        };
    }
}
