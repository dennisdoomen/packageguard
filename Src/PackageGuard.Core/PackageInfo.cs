using System.Text.RegularExpressions;
using JetBrains.Annotations;
using MemoryPack;
using NuGet.Versioning;
using PackageGuard.Core.Common;

namespace PackageGuard.Core;

/// <summary>
/// Represents the normalized metadata, license information, and risk signals known for a single package version.
/// </summary>
[MemoryPackable]
public partial class PackageInfo
{
    /// <summary>
    /// Tracks the projects in the current analysis run that reference this package.
    /// </summary>
    private List<string> projects = new();

    /// <summary>
    /// Gets or sets the package identifier as exposed by the package ecosystem.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized package version being analyzed.
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// Gets or sets the resolved license identifier or friendly license name for the package.
    /// </summary>
    public string? License { get; set; }

    /// <summary>
    /// Gets or sets the source URL where the license text or license metadata can be retrieved.
    /// </summary>
    public string? LicenseUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the package was used in any of the
    /// projects during this run.
    /// </summary>
    /// <remarks>
    /// It is used to clear out old package information before updating the persisted cache.
    /// </remarks>
    [MemoryPackIgnore]
    public bool IsUsed { get; set; }

    /// <summary>
    /// Gets or sets the projects that reference this package in the current analysis run.
    /// </summary>
    public string[] Projects
    {
        get => projects.ToArray();
        [UsedImplicitly]
        set => projects = new List<string>();
    }

    /// <summary>
    /// Gets or sets the name of the package source that was used to fetch the metadata.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the URL of the package source that was used to fetch the metadata.
    /// </summary>
    public string SourceUrl { get; set; } = "";

    /// <summary>
    /// Gets or sets the repository URL used to enrich package metadata and risk information.
    /// </summary>
    public string? RepositoryUrl { get; set; }

    /// <summary>
    /// Gets or sets when the cached metadata for this package was last refreshed.
    /// </summary>
    public DateTimeOffset? CacheUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the overall risk score for this package on a 0-100 scale.
    /// The score is assigned by <see cref="RiskEvaluator"/> as
    /// <c>RiskDimensions.OverallRisk * 10</c>, where <see cref="RiskDimensions.OverallRisk"/>
    /// is the weighted combination of legal risk (20%), security risk (45%), and operational risk (35%).
    /// </summary>
    [MemoryPackIgnore]
    public double RiskScore { get; set; }

    /// <summary>
    /// Gets or sets the individual risk dimension scores and rationales produced by <see cref="RiskEvaluator"/>.
    /// Each dimension is stored on a 0-10 scale before being combined into <see cref="RiskScore"/>.
    /// </summary>
    [MemoryPackIgnore]
    public RiskDimensions RiskDimensions { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the discovered <see cref="LicenseUrl"/> responded successfully when validated.
    /// <see cref="LicenseUrlRiskEnricher"/> sets this to <see langword="false"/> when the URL is missing,
    /// when the request throws, or when the response status is not successful.
    /// </summary>
    public bool? HasValidLicenseUrl { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="LicenseUrlRiskEnricher"/> has already attempted license URL validation.
    /// This is a cache marker for the enrichment pass, not a quality signal by itself.
    /// </summary>
    public bool HasValidatedLicenseUrl { get; set; }

    /// <summary>
    /// Gets or sets whether the package complies with the active license policy.
    /// </summary>
    [MemoryPackIgnore]
    public bool? IsLicensePolicyCompatible { get; set; }

    /// <summary>
    /// Gets or sets the number of direct vulnerabilities returned by OSV for this exact package version.
    /// <see cref="OsvRiskEnricher"/> queries the OSV API by ecosystem, package name, and version,
    /// then stores the number of matching vulnerability entries.
    /// </summary>
    public int VulnerabilityCount { get; set; }

    /// <summary>
    /// Gets or sets the number of reachable dependencies that are themselves vulnerable.
    /// <see cref="TransitiveVulnerabilityCountEnricher"/> walks <see cref="DependencyKeys"/> recursively and counts distinct
    /// transitive packages whose <see cref="VulnerabilityCount"/> is greater than zero.
    /// </summary>
    [MemoryPackIgnore]
    public int TransitiveVulnerabilityCount { get; set; }

    /// <summary>
    /// Gets or sets the highest severity score observed across the OSV vulnerabilities for this package version.
    /// Severity comes from OSV severity entries when present, falling back to textual severity mapping,
    /// and the stored value is the maximum score found.
    /// </summary>
    public double MaxVulnerabilitySeverity { get; set; }

    /// <summary>
    /// Gets or sets whether any OSV vulnerability for this version has both an available fix and a
    /// <c>modified</c> timestamp within the last 90 days.
    /// </summary>
    public bool HasPatchedVulnerabilityInLast90Days { get; set; }

    /// <summary>
    /// Gets or sets the shortest dependency depth discovered for this package in the analyzed graph.
    /// For NuGet, <see cref="CSharp.CSharpProjectAnalysisStrategy"/> computes this with a breadth-first
    /// walk from direct project dependencies starting at depth 1.
    /// </summary>
    [MemoryPackIgnore]
    public int DependencyDepth { get; set; }

    /// <summary>
    /// Gets or sets whether the local <c>.nupkg</c> archive contains a NuGet signature file.
    /// <see cref="NuGetPackageSigningRiskEnricher"/> sets this from the presence of
    /// <c>.signature.p7s</c> inside the package archive.
    /// </summary>
    public bool? IsPackageSigned { get; set; }

    /// <summary>
    /// Gets or sets whether NuGet trust verification succeeded for the package signature.
    /// When the package is signed, <see cref="NuGetPackageSigningRiskEnricher"/> runs
    /// <c>dotnet nuget verify --all</c>; a zero exit code becomes <see langword="true"/>,
    /// a non-zero exit code becomes <see langword="false"/>, and timeouts or tool failures leave it <see langword="null"/>.
    /// </summary>
    public bool? HasTrustedPackageSignature { get; set; }

    /// <summary>
    /// Gets or sets whether archive-inspection signing metadata has already been collected for this package.
    /// This is the cache marker used by <see cref="NuGetPackageSigningRiskEnricher"/>.
    /// </summary>
    public bool HasSigningRiskData { get; set; }

    /// <summary>
    /// Gets or sets whether the upstream ecosystem marks this package version as deprecated.
    /// NuGet uses metadata heuristics, while npm reads the version-level <c>deprecated</c> field.
    /// </summary>
    public bool? IsDeprecated { get; set; }

    /// <summary>
    /// Gets or sets when the analyzed package version was published upstream.
    /// For NuGet this comes from package search metadata; for npm it comes from the registry <c>time</c> map.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// Gets or sets the ecosystem download count reported for this package.
    /// The exact meaning depends on the feed: NuGet exposes package download metadata, while npm uses registry download statistics.
    /// </summary>
    public long? DownloadCount { get; set; }

    /// <summary>
    /// Gets or sets whether the GitHub repository owner is an organization account.
    /// <see cref="GitHubRepositoryRiskEnricher"/> uses the repository owner type returned by the GitHub API.
    /// </summary>
    public bool OwnerIsOrganization { get; set; }

    /// <summary>
    /// Gets or sets when the GitHub repository owner account was created, as reported by the GitHub API.
    /// </summary>
    public DateTimeOffset? OwnerCreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of non-bot contributors returned by the GitHub contributors API.
    /// Bot, Dependabot, and Copilot identities are excluded before counting.
    /// </summary>
    public int? ContributorCount { get; set; }

    /// <summary>
    /// Gets or sets the concentration of work in the most active contributor.
    /// It is calculated as that contributor's contribution count divided by the total
    /// contribution count across the filtered contributor sample.
    /// </summary>
    public double? TopContributorShare { get; set; }

    /// <summary>
    /// Gets or sets the concentration of work in the top two contributors.
    /// It is calculated as the sum of the two largest contributor counts divided by the total
    /// contribution count across the filtered contributor sample.
    /// </summary>
    public double? TopTwoContributorShare { get; set; }

    /// <summary>
    /// Gets or sets the number of non-bot maintainers with at least one commit in the last six months.
    /// <see cref="GitHubRepositoryRiskEnricher"/> derives this from the most recent commit timestamp
    /// seen per maintainer in the last 12 months of default-branch history.
    /// </summary>
    public int? RecentMaintainerCount { get; set; }

    /// <summary>
    /// Gets or sets whether the repository root contains a README-like file according to the GitHub contents API.
    /// </summary>
    public bool? HasReadme { get; set; }

    /// <summary>
    /// Gets or sets whether the discovered README looks like boilerplate instead of project-specific documentation.
    /// This is based on simple content heuristics in <see cref="GitHubRepositoryRiskEnricher"/>.
    /// </summary>
    public bool? HasDefaultReadme { get; set; }

    /// <summary>
    /// Gets or sets the date of the latest default-branch commit touching the README file.
    /// </summary>
    public DateTimeOffset? ReadmeUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets whether the repository root contains a contributing guide such as <c>CONTRIBUTING.md</c>.
    /// </summary>
    public bool? HasContributingGuide { get; set; }

    /// <summary>
    /// Gets or sets whether a security policy file was found in the repository root or under <c>.github</c>.
    /// </summary>
    public bool? HasSecurityPolicy { get; set; }

    /// <summary>
    /// Gets or sets whether the discovered security policy appears detailed enough to be actionable.
    /// The current heuristic requires substantial content and an explicit contact channel.
    /// </summary>
    public bool? HasDetailedSecurityPolicy { get; set; }

    /// <summary>
    /// Gets or sets whether the security policy appears to describe coordinated disclosure.
    /// The current heuristic looks for a contact/reporting channel plus a private or encrypted reporting path.
    /// </summary>
    public bool? HasCoordinatedDisclosure { get; set; }

    /// <summary>
    /// Gets or sets whether the repository root contains a changelog file.
    /// </summary>
    public bool? HasChangelog { get; set; }

    /// <summary>
    /// Gets or sets whether the discovered changelog looks like boilerplate instead of maintained release history.
    /// </summary>
    public bool? HasDefaultChangelog { get; set; }

    /// <summary>
    /// Gets or sets the date of the latest default-branch commit touching the changelog file.
    /// </summary>
    public DateTimeOffset? ChangelogUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of open GitHub issues labeled <c>bug</c>, excluding pull requests.
    /// </summary>
    public int? OpenBugIssueCount { get; set; }

    /// <summary>
    /// Gets or sets the number of open critical bug issues that remain stale according to the GitHub issue heuristics.
    /// This is populated by <see cref="GitHubRepositoryRiskEnricher"/> from the recent open-bug sample.
    /// </summary>
    public int? StaleCriticalBugIssueCount { get; set; }

    /// <summary>
    /// Gets or sets the median maintainer response time for open bug issues, in days.
    /// Each sample uses the time from issue creation to the first comment by an owner, member, or collaborator.
    /// </summary>
    public double? MedianIssueResponseDays { get; set; }

    /// <summary>
    /// Gets or sets the median maintainer response time for critical open bug issues, in days.
    /// It uses the same first-maintainer-comment calculation as <see cref="MedianIssueResponseDays"/>,
    /// but only on issues flagged as critical.
    /// </summary>
    public double? MedianCriticalIssueResponseDays { get; set; }

    /// <summary>
    /// Gets or sets the share of sampled open bug issues that received at least one maintainer response.
    /// This is <c>respondedIssueCount / openIssueCount</c> for the GitHub issue sample.
    /// </summary>
    public double? IssueResponseCoverage { get; set; }

    /// <summary>
    /// Gets or sets the median age in days of the sampled open bug issues at analysis time.
    /// </summary>
    public double? MedianOpenBugAgeDays { get; set; }

    /// <summary>
    /// Gets or sets the number of bug issues closed within the last 90 days.
    /// The value comes from the closed-bug GitHub issue sample filtered by <c>closed_at</c>.
    /// </summary>
    public int? ClosedBugIssueCountLast90Days { get; set; }

    /// <summary>
    /// Gets or sets the number of recently closed bug issues whose issue timeline contains a <c>reopened</c> event.
    /// The current calculation inspects up to 20 of the recently closed bug issues from the last 90 days.
    /// </summary>
    public int? ReopenedBugIssueCountLast90Days { get; set; }

    /// <summary>
    /// Gets or sets the median time in days from pull request creation to merge.
    /// <see cref="GitHubRepositoryRiskEnricher"/> computes this from the 100 most recently updated closed pull requests
    /// that have a <c>merged_at</c> timestamp.
    /// </summary>
    public double? MedianPullRequestMergeDays { get; set; }

    /// <summary>
    /// Gets or sets whether any immediate dependency in the analyzed graph is a major-zero package.
    /// For NuGet this is derived from the lock file by checking whether a package directly depends on a dependency whose major version is 0.
    /// </summary>
    public bool HasPreOneZeroDependencies { get; set; }

    /// <summary>
    /// Gets or sets the dependency keys of the package's immediate dependencies.
    /// These keys are later used by <see cref="ProjectAnalyzer"/> for recursive transitive-health calculations.
    /// </summary>
    [MemoryPackIgnore]
    public string[] DependencyKeys { get; set; } = [];

    /// <summary>
    /// Gets or sets whether at least one OSV vulnerability advertises a fix event.
    /// This is based on the vulnerability's affected-range event list, not on whether the current project has upgraded yet.
    /// </summary>
    public bool HasAvailableSecurityFix { get; set; }

    /// <summary>
    /// Gets or sets the median number of days between OSV <c>published</c> and <c>modified</c> timestamps
    /// for vulnerabilities that have a recorded fix event.
    /// </summary>
    public double? MedianVulnerabilityFixDays { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="OsvRiskEnricher"/> has already populated OSV-derived vulnerability metadata.
    /// </summary>
    public bool HasOsvRiskData { get; set; }

    /// <summary>
    /// Gets or sets the latest stable version known for this package in its ecosystem.
    /// NuGet chooses the highest non-prerelease version from search metadata; npm uses the registry <c>dist-tags.latest</c> value.
    /// </summary>
    public string? LatestStableVersion { get; set; }

    /// <summary>
    /// Gets or sets when <see cref="LatestStableVersion"/> was published upstream.
    /// </summary>
    public DateTimeOffset? LatestStablePublishedAt { get; set; }

    /// <summary>
    /// Gets or sets how far this version trails the latest stable release in publication time.
    /// The value is calculated as <c>LatestStablePublishedAt - PublishedAt</c> when the latest stable release is newer.
    /// </summary>
    public double? VersionUpdateLagDays { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="LatestStableVersion"/> has a higher major version than <see cref="Version"/>.
    /// </summary>
    public bool IsMajorVersionBehindLatest { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="LatestStableVersion"/> is newer than <see cref="Version"/>
    /// while staying within the same major version.
    /// </summary>
    public bool IsMinorVersionBehindLatest { get; set; }

    /// <summary>
    /// Gets or sets the number of recent completed workflow runs on the default branch whose conclusion was <c>failure</c>.
    /// The current sample inspects up to 10 recent GitHub Actions runs.
    /// </summary>
    public int? RecentFailedWorkflowCount { get; set; }

    /// <summary>
    /// Gets or sets whether at least one of the sampled recent workflow runs completed successfully.
    /// </summary>
    public bool? HasRecentSuccessfulWorkflowRun { get; set; }

    /// <summary>
    /// Gets or sets the recent workflow failure rate, calculated as
    /// <c>failedCompletedRuns / completedRuns</c> in the sampled GitHub Actions history.
    /// </summary>
    public double? WorkflowFailureRate { get; set; }

    /// <summary>
    /// Gets or sets whether the sampled workflow history suggests flakiness.
    /// The current heuristic requires at least four completed runs with both failures and successes present,
    /// but not all runs failing.
    /// </summary>
    public bool? HasFlakyWorkflowPattern { get; set; }

    /// <summary>
    /// Gets or sets the number of required status-check contexts configured on the default branch protection rule.
    /// </summary>
    public int? RequiredStatusCheckCount { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct CI platforms mentioned in workflow files.
    /// The current heuristic counts mentions of Ubuntu, Windows, macOS, and self-hosted runners.
    /// </summary>
    public int? WorkflowPlatformCount { get; set; }

    /// <summary>
    /// Gets or sets whether workflow files contain coverage-related signals such as
    /// <c>coverage</c>, <c>codecov</c>, <c>coveralls</c>, or <c>coverlet</c>.
    /// </summary>
    public bool? HasCoverageWorkflowSignal { get; set; }

    /// <summary>
    /// Gets or sets whether workflow files contain reproducible-build signals such as
    /// <c>deterministic</c>, <c>reproducible</c>, or <c>source-build</c>.
    /// </summary>
    public bool? HasReproducibleBuildSignal { get; set; }

    /// <summary>
    /// Gets or sets whether dependency update automation appears to be configured.
    /// The current heuristic looks for Dependabot, Renovate, or related workflow/configuration signals.
    /// </summary>
    public bool? HasDependencyUpdateAutomation { get; set; }

    /// <summary>
    /// Gets or sets whether workflow files or repository layout suggest automated test execution.
    /// The current heuristic looks for test commands in CI files and common test directory names.
    /// </summary>
    public bool? HasTestSignal { get; set; }

    /// <summary>
    /// Gets or sets the overall OpenSSF Scorecard score returned for the GitHub repository.
    /// </summary>
    public double? OpenSsfScore { get; set; }

    /// <summary>
    /// Gets or sets whether branch protection appears to be enabled for the default branch.
    /// This is sourced from the GitHub branch API and may also be corroborated by OpenSSF Scorecard data.
    /// </summary>
    public bool? HasBranchProtection { get; set; }

    /// <summary>
    /// Gets or sets whether workflow files appear to publish provenance or attestation artifacts.
    /// The current heuristic looks for terms such as <c>slsa</c>, <c>provenance</c>, or <c>attest</c>.
    /// </summary>
    public bool? HasProvenanceAttestation { get; set; }

    /// <summary>
    /// Gets or sets whether the package-declared GitHub owner/repository differs from the repository's canonical GitHub URL.
    /// This is a rename or ownership-transfer heuristic based on normalized <c>owner/repo</c> identifiers.
    /// </summary>
    public bool? HasRepositoryOwnershipOrRenameChurn { get; set; }

    /// <summary>
    /// Gets or sets whether recent GitHub releases expose a verified signature signal.
    /// The value is taken from the first sampled release that reports signature verification metadata.
    /// </summary>
    public bool? HasVerifiedReleaseSignature { get; set; }

    /// <summary>
    /// Gets or sets whether PackageGuard found a trustworthy publisher signal.
    /// For NuGet this is seeded from trusted package-signature verification; for GitHub-backed data it falls back to the heuristic that organization-owned repositories provide a stronger publisher signal than personal accounts.
    /// </summary>
    public bool? HasVerifiedPublisher { get; set; }

    /// <summary>
    /// Gets or sets the fraction of sampled releases whose GitHub <c>prerelease</c> flag is set.
    /// </summary>
    public double? PrereleaseRatio { get; set; }

    /// <summary>
    /// Gets or sets the number of adjacent sampled releases published within three days of each other.
    /// This is used as a heuristic for rapid corrective follow-up releases.
    /// </summary>
    public int? RapidReleaseCorrectionCount { get; set; }

    /// <summary>
    /// Gets or sets whether at least one sampled GitHub release contains a non-trivial body.
    /// The current heuristic treats release notes as present when a release body exists and is at least 80 characters long.
    /// </summary>
    public bool? HasReleaseNotes { get; set; }

    /// <summary>
    /// Gets or sets whether every sampled release tag parses as a semantic version.
    /// If any sampled release tag does not parse, this becomes <see langword="false"/>.
    /// </summary>
    public bool? HasSemVerReleaseTags { get; set; }

    /// <summary>
    /// Gets or sets the average number of days between sampled release publication dates.
    /// </summary>
    public double? MeanReleaseIntervalDays { get; set; }

    /// <summary>
    /// Gets or sets the share of sampled release-to-release transitions that move to a higher major version.
    /// This is derived from semver-parsable releases ordered by publication date, so stable releases that stay on the same major line no longer look risky by themselves.
    /// </summary>
    public double? MajorReleaseRatio { get; set; }

    /// <summary>
    /// Gets or sets the share of sampled merged pull requests authored by external contributors.
    /// External status is based on GitHub author-association values outside the maintainer set.
    /// </summary>
    public double? ExternalContributionRate { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct non-bot reviewers seen across the sampled recent merged pull requests.
    /// Review lookups are currently capped to the first 20 merged pull requests in the sample.
    /// </summary>
    public int? UniqueReviewerCount { get; set; }

    /// <summary>
    /// Gets or sets reviewer diversity as <c>uniqueReviewerCount / mergedPullRequestCount</c>
    /// for the sampled recent merged pull requests.
    /// </summary>
    public double? ReviewerDiversityRatio { get; set; }

    /// <summary>
    /// Gets or sets the share of sampled default-branch commits whose GitHub verification state is <c>verified</c>.
    /// Only commits with explicit verification metadata are included in the denominator.
    /// </summary>
    public double? VerifiedCommitRatio { get; set; }

    /// <summary>
    /// Gets or sets the median number of days since each known maintainer's most recent commit activity.
    /// The sample is built from the last 12 months of default-branch commits after excluding bot identities.
    /// </summary>
    public double? MedianMaintainerActivityDays { get; set; }

    /// <summary>
    /// Gets or sets the share of sampled open bug issues that received a maintainer response within seven days.
    /// This is <c>triagedWithinSevenDaysCount / openIssueCount</c>.
    /// </summary>
    public double? IssueTriageWithinSevenDaysRate { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="GitHubRepositoryRiskEnricher"/> has already populated GitHub-derived repository risk metadata.
    /// </summary>
    public bool HasGitHubRiskData { get; set; }

    /// <summary>
    /// Gets or sets the target frameworks discovered in the package archive's <c>lib/</c> and <c>ref/</c> folders.
    /// This is populated by <see cref="NuGetPackageSigningRiskEnricher"/> during local archive inspection.
    /// </summary>
    public string[] SupportedTargetFrameworks { get; set; } = [];

    /// <summary>
    /// Gets or sets whether <see cref="SupportedTargetFrameworks"/> contains at least one framework
    /// considered modern by the current heuristic: <c>net5+</c>, <c>netstandard2.0</c>, or <c>netstandard2.1</c>.
    /// </summary>
    public bool? HasModernTargetFrameworkSupport { get; set; }

    /// <summary>
    /// Gets or sets whether the package archive contains native or platform-specific binary assets.
    /// The current heuristic looks for <c>/native/</c> paths and binary file extensions such as
    /// <c>.so</c>, <c>.dylib</c>, <c>.a</c>, <c>.lib</c>, and <c>.exe</c>.
    /// </summary>
    public bool? HasNativeBinaryAssets { get; set; }

    /// <summary>
    /// Gets or sets the number of reachable transitive dependencies whose published version is older than 24 months.
    /// <see cref="DependencyHealthCountEnricher"/> calculates this recursively over <see cref="DependencyKeys"/>.
    /// </summary>
    [MemoryPackIgnore]
    public int? StaleTransitiveDependencyCount { get; set; }

    /// <summary>
    /// Gets or sets the number of reachable transitive dependencies that look both stale and risky.
    /// The current heuristic requires a dependency to be older than 24 months and to have either
    /// weak maintainer signals or known security concerns.
    /// </summary>
    [MemoryPackIgnore]
    public int? AbandonedTransitiveDependencyCount { get; set; }

    /// <summary>
    /// Gets or sets the number of reachable transitive dependencies marked as deprecated by their ecosystem metadata.
    /// </summary>
    [MemoryPackIgnore]
    public int? DeprecatedTransitiveDependencyCount { get; set; }

    /// <summary>
    /// Gets or sets the number of reachable transitive dependencies that look both stale and critically vulnerable.
    /// The current heuristic requires a dependency older than 24 months with at least one vulnerability
    /// whose maximum severity is 7.0 or higher.
    /// </summary>
    [MemoryPackIgnore]
    public int? UnmaintainedCriticalTransitiveDependencyCount { get; set; }

    /// <summary>
    /// Determines whether this package matches the provided package name and optional version range.
    /// </summary>
    public bool SatisfiesRange(string name, string? versionRange = null)
    {
        if (!name.Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (versionRange is null)
        {
            return true;
        }

        var range = VersionRange.Parse(versionRange);
        return range.Satisfies(NuGetVersion.Parse(Version));
    }

    /// <summary>
    /// Records that the package was referenced by the specified project during this run.
    /// </summary>
    public void TrackAsUsedInProject(string projectPath)
    {
        projects.Add(projectPath);
    }

    /// <summary>
    /// Builds the cache key that uniquely identifies this package entry within a source-specific collection.
    /// </summary>
    internal string GetCollectionKey() => CreateCollectionKey(SourceUrl, Name, Version);

    /// <summary>
    /// Builds the dependency key that identifies this package across dependency graphs.
    /// </summary>
    internal string CreatePackageKey() => CreateDependencyKey(GetPackageEcosystem(), Name, Version);

    /// <summary>
    /// Builds the NuGet dependency key for a package with the given <paramref name="name"/> and <paramref name="version"/>.
    /// </summary>
    internal static string CreatePackageKey(string name, string version) => CreateDependencyKey("nuget", name, version);

    /// <summary>
    /// Builds the collection key used to distinguish packages by source URL, name, and version.
    /// </summary>
    internal static string CreateCollectionKey(string sourceUrl, string name, string version) => $"{sourceUrl}|{name}|{version}";

    /// <summary>
    /// Builds the dependency key used to distinguish packages by ecosystem, name, and version.
    /// </summary>
    internal static string CreateDependencyKey(string ecosystem, string name, string version) => $"{ecosystem}|{name}|{version}";

    /// <summary>
    /// Determines the package ecosystem used when building dependency keys.
    /// </summary>
    private string GetPackageEcosystem()
    {
        if (Source.Equals("npm", StringComparison.OrdinalIgnoreCase) ||
            SourceUrl.Contains("npmjs.org", StringComparison.OrdinalIgnoreCase) ||
            SourceUrl.Contains("yarnpkg.com", StringComparison.OrdinalIgnoreCase))
        {
            return "npm";
        }

        return "nuget";
    }

    /// <summary>
    /// Returns a concise textual representation of the package including its version and resolved license.
    /// </summary>
    public override string ToString() => $"{Name}/{Version} ({License})";

    /// <summary>
    /// Returns <c>true</c> if the name or URL of the feed where this package was found matches the given wildcard.
    /// </summary>
    public bool MatchesFeed(string feedWildcard)
    {
        return Source.MatchesWildcard(feedWildcard) || SourceUrl.MatchesWildcard(feedWildcard);
    }

    /// <summary>
    /// Marks the current package as being used by the current projects.
    /// </summary>
    public void MarkAsUsed()
    {
        IsUsed = true;
    }
}
