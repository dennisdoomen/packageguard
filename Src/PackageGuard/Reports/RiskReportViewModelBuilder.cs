using System.Globalization;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Package;
using Pathy;

namespace PackageGuard.Reports;

/// <summary>
/// Turns the risk data of a package into the evidence and links that the risk report shows, without any markup,
/// so that the content of the report can be tested without parsing HTML.
/// </summary>
internal static class RiskReportViewModelBuilder
{
    /// <summary>
    /// Maps the start of a rationale to the function that returns the URL that rationale links to.
    /// </summary>
    private static readonly (string Prefix, Func<PackageInfo, string?> GetUrl)[] RationaleLinks =
    [
        ("License URL", TryGetLicenseFileUrl),
        ("Public repository available", TryGetRepositoryUrl),
        ("README looks present and non-default", TryGetReadmeUrl),
        ("CONTRIBUTING guide is present", TryGetContributingGuideUrl),
        ("CHANGELOG or release notes are present", TryGetReleaseHistoryUrl),
        ("OpenSSF Scorecard score is low", TryGetOpenSsfScorecardUrl)
    ];

    /// <summary>
    /// Returns a URL with more information about the license of the package. A single SPDX identifier links to
    /// its page on spdx.org. Otherwise, the package's own license URL is used when it is a valid web address.
    /// </summary>
    internal static string? TryGetLicenseInfoUrl(string? license, string? licenseUrl)
    {
        license = license?.Trim();
        if (!string.IsNullOrEmpty(license) && IsSingleSpdxIdentifier(license))
        {
            return $"https://spdx.org/licenses/{Uri.EscapeDataString(license)}.html";
        }

        if (Uri.TryCreate(licenseUrl, UriKind.Absolute, out Uri? licenseUri) &&
            (licenseUri.Scheme is "http" or "https"))
        {
            return licenseUri.ToString();
        }

        return null;
    }

    /// <summary>
    /// Returns <c>true</c> for a plain SPDX identifier such as <c>MIT</c> or <c>Apache-2.0</c>, and <c>false</c>
    /// for expressions, free text and placeholders.
    /// </summary>
    private static bool IsSingleSpdxIdentifier(string license) =>
        !license.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
        IsWellKnownLicense(license) &&
        license.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '+');

    /// <summary>
    /// Returns the evidence of a package in titled groups, each rendered as its own card in the detail section.
    /// Groups without any evidence are left out.
    /// </summary>
    internal static IReadOnlyCollection<EvidenceGroup> BuildEvidenceGroups(PackageInfo package)
    {
        EvidenceGroup[] groups =
        [
            new("Package", BuildIdentityDetails(package).ToArray()),
            new("Security", BuildSecurityDetails(package).ToArray()),
            new("Maintenance", BuildOperationalDetails(package).ToArray()),
            new("Releases", BuildReleaseDetails(package).ToArray())
        ];

        return groups.Where(group => group.Entries.Count > 0).ToArray();
    }

    /// <summary>
    /// Returns the URL that a rationale of <paramref name="package"/> links to, such as its license file, repository,
    /// README, contributing guide, release history or OpenSSF scorecard, or <c>null</c> when it has none.
    /// </summary>
    internal static string? TryGetRationaleUrl(PackageInfo package, string rationale) =>
        RationaleLinks
            .Where(link => rationale.StartsWith(link.Prefix, StringComparison.Ordinal))
            .Select(link => link.GetUrl(package))
            .FirstOrDefault(url => url is not null);

    /// <summary>
    /// Returns what identifies the package: where it is used, its license, versions, downloads and target frameworks.
    /// </summary>
    private static IEnumerable<EvidenceEntry> BuildIdentityDetails(PackageInfo package)
    {
        string[] displayProjectPaths = GetDisplayProjectPaths(package);
        if (displayProjectPaths.Length > 0)
        {
            yield return ("Used by", string.Join(", ", displayProjectPaths));
        }

        yield return new EvidenceEntry("License", FormatLicenseDisplay(package),
            Url: TryGetLicenseInfoUrl(package.License, package.LicenseUrl));

        if (package.IsDeprecated != null)
        {
            yield return ("Deprecated package version", package.IsDeprecated.Value ? "Yes" : "No");
        }

        if (!string.IsNullOrWhiteSpace(package.LatestStableVersion))
        {
            yield return ("Latest stable version", package.LatestStableVersion);
        }

        if (package.LatestStablePublishedAt != null)
        {
            yield return ("Latest stable published",
                package.LatestStablePublishedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.VersionUpdateLagDays != null)
        {
            yield return ("Version update lag", $"{RiskDisplay.FormatDecimal(package.VersionUpdateLagDays.Value)} days");
        }

        if (package.PublishedAt != null)
        {
            yield return ("Published", package.PublishedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.DownloadCount != null)
        {
            yield return ("Downloads", package.DownloadCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.SupportedTargetFrameworks.Length > 0)
        {
            yield return ("Target frameworks", string.Join(", ", package.SupportedTargetFrameworks));
        }
    }

    /// <summary>
    /// Returns the security evidence: signatures, vulnerabilities, risky transitive dependencies and security policies.
    /// </summary>
    private static IEnumerable<EvidenceEntry> BuildSecurityDetails(PackageInfo package)
    {
        if (package.IsPackageSigned != null)
        {
            yield return ("Package signature", package.IsPackageSigned.Value ? "Signed" : "Unsigned");
        }

        if (package.HasTrustedPackageSignature != null)
        {
            yield return ("Signature trust", package.HasTrustedPackageSignature.Value ? "Verified" : "Unverified");
        }

        if (package.HasVerifiedPublisher != null)
        {
            yield return ("Verified publisher", package.HasVerifiedPublisher.Value ? "Detected" : "Not detected");
        }

        if (package.HasVerifiedReleaseSignature != null)
        {
            yield return ("Verified release signature", package.HasVerifiedReleaseSignature.Value ? "Detected" : "Not detected");
        }

        if (package.VerifiedCommitRatio != null)
        {
            yield return ("Verified commit coverage",
                package.VerifiedCommitRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.VulnerabilityCount > 0)
        {
            yield return new EvidenceEntry(
                "Vulnerabilities",
                $"{package.VulnerabilityCount} (max severity {RiskDisplay.FormatDecimal(package.MaxVulnerabilitySeverity)})",
                package.ActiveVulnerabilities.Select(vulnerability => vulnerability.Id).ToArray());
        }

        if (package.MaliciousAdvisoryIds.Length > 0)
        {
            yield return ("Malicious package advisories", string.Join(", ", package.MaliciousAdvisoryIds));
        }

        if (package.MaxEpssScore is { } epss)
        {
            yield return ("Highest EPSS exploitation probability (30 days)",
                $"{(epss * 100).ToString("0.0##", CultureInfo.InvariantCulture)}%");
        }

        if (package.KnownExploitedCveIds is { Length: > 0 } exploitedCveIds)
        {
            yield return new EvidenceEntry("Known exploited (CISA KEV)",
                exploitedCveIds.Length.ToString(CultureInfo.InvariantCulture),
                exploitedCveIds);
        }

        if (package.MedianVulnerabilityFixDays != null)
        {
            yield return ("Median advisory publication-to-update time",
                $"{RiskDisplay.FormatDecimal(package.MedianVulnerabilityFixDays.Value)} days");
        }

        if (package.DependencyDepth > 0)
        {
            yield return ("Dependency depth", package.DependencyDepth.ToString(CultureInfo.InvariantCulture));
        }

        if (package.TransitiveVulnerabilityCount > 0)
        {
            yield return new EvidenceEntry(
                "Transitive vulnerabilities",
                package.TransitiveVulnerabilityCount.ToString(CultureInfo.InvariantCulture),
                package.VulnerableTransitiveDependencyDetails);
        }

        if (package.StaleTransitiveDependencyCount != null)
        {
            yield return new EvidenceEntry(
                "Stale transitive dependencies",
                package.StaleTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture),
                package.StaleTransitiveDependencyDetails);
        }

        if (package.AbandonedTransitiveDependencyCount != null)
        {
            yield return new EvidenceEntry(
                "Potentially abandoned transitive dependencies",
                package.AbandonedTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture),
                package.AbandonedTransitiveDependencyDetails);
        }

        if (package.DeprecatedTransitiveDependencyCount != null)
        {
            yield return new EvidenceEntry(
                "Deprecated transitive dependencies",
                package.DeprecatedTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture),
                package.DeprecatedTransitiveDependencyDetails);
        }

        if (package.UnmaintainedCriticalTransitiveDependencyCount != null)
        {
            yield return new EvidenceEntry(
                "Unmaintained critical transitives",
                package.UnmaintainedCriticalTransitiveDependencyCount.Value.ToString(CultureInfo.InvariantCulture),
                package.UnmaintainedCriticalTransitiveDependencyDetails);
        }

        if (package.HasNativeBinaryAssets != null)
        {
            yield return ("Native/binary assets", package.HasNativeBinaryAssets.Value ? "Detected" : "Not detected");
        }

        if (package.HasSecurityPolicy != null)
        {
            yield return ("Security policy", package.HasSecurityPolicy == true ? "Present" : "Missing");
        }

        if (package.HasDetailedSecurityPolicy != null)
        {
            yield return ("Detailed security policy", package.HasDetailedSecurityPolicy == true ? "Detected" : "Not detected");
        }

        if (package.HasCoordinatedDisclosure != null)
        {
            yield return ("Coordinated disclosure guidance",
                package.HasCoordinatedDisclosure == true ? "Detected" : "Not detected");
        }

        if (package.HasProvenanceAttestation != null)
        {
            yield return ("Provenance/attestation", package.HasProvenanceAttestation == true ? "Detected" : "Not detected");
        }
    }

    /// <summary>
    /// Returns the maintenance evidence: contributors, issue handling and the health of the CI workflows.
    /// </summary>
    private static IEnumerable<EvidenceEntry> BuildOperationalDetails(PackageInfo package)
    {
        if (package.ContributorCount != null)
        {
            yield return ("Contributors", package.ContributorCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.RecentMaintainerCount != null)
        {
            yield return ("Active maintainers (6 months)",
                package.RecentMaintainerCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.MedianMaintainerActivityDays != null)
        {
            yield return ("Median maintainer inactivity",
                $"{RiskDisplay.FormatDecimal(package.MedianMaintainerActivityDays.Value)} days");
        }

        if (package.TopContributorShare != null)
        {
            yield return ("Top contributor share",
                package.TopContributorShare.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.OpenBugIssueCount != null)
        {
            yield return ("Open bug issues", package.OpenBugIssueCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ClosedBugIssueCountLast90Days != null)
        {
            yield return ("Closed bug issues (90 days)",
                package.ClosedBugIssueCountLast90Days.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ReopenedBugIssueCountLast90Days != null)
        {
            yield return ("Reopened bug issues (90 days)",
                package.ReopenedBugIssueCountLast90Days.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.MedianIssueResponseDays != null)
        {
            yield return ("Median issue response", $"{RiskDisplay.FormatDecimal(package.MedianIssueResponseDays.Value)} days");
        }

        if (package.MedianCriticalIssueResponseDays != null)
        {
            yield return ("Median critical issue response",
                $"{RiskDisplay.FormatDecimal(package.MedianCriticalIssueResponseDays.Value)} days");
        }

        if (package.IssueResponseCoverage != null)
        {
            yield return ("Issue response coverage",
                package.IssueResponseCoverage.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.IssueTriageWithinSevenDaysRate != null)
        {
            yield return ("Issue triage within 7 days",
                package.IssueTriageWithinSevenDaysRate.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.MedianOpenBugAgeDays != null)
        {
            yield return ("Median open bug age", $"{RiskDisplay.FormatDecimal(package.MedianOpenBugAgeDays.Value)} days");
        }

        foreach (EvidenceEntry item in BuildWorkflowDetails(package))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Returns the evidence about the CI workflows, tests and branch protection of the repository.
    /// </summary>
    private static IEnumerable<EvidenceEntry> BuildWorkflowDetails(PackageInfo package)
    {
        if (package.RecentFailedWorkflowCount != null)
        {
            yield return ("Recent failed workflows",
                package.RecentFailedWorkflowCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.WorkflowFailureRate != null)
        {
            yield return ("Workflow failure rate",
                package.WorkflowFailureRate.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.HasFlakyWorkflowPattern != null)
        {
            yield return ("Flaky workflow pattern", package.HasFlakyWorkflowPattern == true ? "Detected" : "Not detected");
        }

        if (package.RequiredStatusCheckCount != null)
        {
            yield return ("Required status checks",
                package.RequiredStatusCheckCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.WorkflowPlatformCount != null)
        {
            yield return ("Workflow platform count", package.WorkflowPlatformCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.HasCoverageWorkflowSignal != null)
        {
            yield return ("Coverage workflow signal", package.HasCoverageWorkflowSignal == true ? "Detected" : "Not detected");
        }

        if (package.HasReproducibleBuildSignal != null)
        {
            yield return ("Reproducible-build signal", package.HasReproducibleBuildSignal == true ? "Detected" : "Not detected");
        }

        if (package.HasDependencyUpdateAutomation != null)
        {
            yield return ("Dependency update automation",
                package.HasDependencyUpdateAutomation == true ? "Detected" : "Not detected");
        }

        if (package.HasTestSignal != null)
        {
            yield return ("Test execution signal", package.HasTestSignal == true ? "Detected" : "Not detected");
        }

        if (package.HasBranchProtection != null)
        {
            yield return ("Branch protection", package.HasBranchProtection == true ? "Detected" : "Not detected");
        }
    }

    /// <summary>
    /// Returns the release evidence: documentation updates, release cadence, release notes and code reviews.
    /// </summary>
    private static IEnumerable<EvidenceEntry> BuildReleaseDetails(PackageInfo package)
    {
        if (package.ReadmeUpdatedAt != null)
        {
            yield return ("README updated", package.ReadmeUpdatedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.ChangelogUpdatedAt != null)
        {
            yield return ("CHANGELOG updated",
                package.ChangelogUpdatedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (package.PrereleaseRatio != null)
        {
            yield return ("Prerelease ratio", package.PrereleaseRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.MeanReleaseIntervalDays != null)
        {
            yield return ("Mean release interval", $"{RiskDisplay.FormatDecimal(package.MeanReleaseIntervalDays.Value)} days");
        }

        if (package.HasReleaseNotes != null)
        {
            yield return ("Release notes signal", package.HasReleaseNotes == true ? "Detected" : "Not detected");
        }

        if (package.HasSemVerReleaseTags != null)
        {
            yield return ("Semantic version release tags", package.HasSemVerReleaseTags == true ? "Consistent" : "Inconsistent");
        }

        if (package.MajorReleaseRatio != null)
        {
            yield return ("Major-version jump ratio",
                package.MajorReleaseRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.RapidReleaseCorrectionCount != null)
        {
            yield return ("Rapid release corrections",
                package.RapidReleaseCorrectionCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ExternalContributionRate != null)
        {
            yield return ("External contribution rate",
                package.ExternalContributionRate.Value.ToString("P0", CultureInfo.InvariantCulture));
        }

        if (package.UniqueReviewerCount != null)
        {
            yield return ("Unique reviewers", package.UniqueReviewerCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (package.ReviewerDiversityRatio != null)
        {
            yield return ("Reviewer diversity ratio",
                package.ReviewerDiversityRatio.Value.ToString("P0", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Returns the validated repository URL for the package, or <c>null</c> if none is available or valid.
    /// </summary>
    private static string? TryGetRepositoryUrl(PackageInfo package)
    {
        if (Uri.TryCreate(package.RepositoryUrl, UriKind.Absolute, out Uri? repositoryUri))
        {
            return repositoryUri.ToString();
        }

        return null;
    }

    /// <summary>
    /// Returns a URL pointing to the license file on GitHub, or falls back to the package's license URL,
    /// or <c>null</c> if neither is resolvable.
    /// </summary>
    private static string? TryGetLicenseFileUrl(PackageInfo package)
    {
        string? repositoryRoot = GitHubRepositoryUrl.TryGetWebRoot(package.RepositoryUrl);
        if (repositoryRoot is not null)
        {
            return $"{repositoryRoot}/blob/HEAD/LICENSE";
        }

        if (Uri.TryCreate(package.LicenseUrl, UriKind.Absolute, out Uri? licenseUri))
        {
            return licenseUri.ToString();
        }

        return null;
    }

    /// <summary>
    /// Returns a URL pointing to the README anchor on the GitHub repository, or <c>null</c> if the repository
    /// URL is not a recognised GitHub URL.
    /// </summary>
    private static string? TryGetReadmeUrl(PackageInfo package)
    {
        string? repositoryRoot = GitHubRepositoryUrl.TryGetWebRoot(package.RepositoryUrl);
        return repositoryRoot is null ? null : $"{repositoryRoot}#readme";
    }

    /// <summary>
    /// Returns a URL pointing to the CONTRIBUTING.md file on the GitHub repository, or <c>null</c> if the
    /// repository URL is not a recognised GitHub URL.
    /// </summary>
    private static string? TryGetContributingGuideUrl(PackageInfo package)
    {
        string? repositoryRoot = GitHubRepositoryUrl.TryGetWebRoot(package.RepositoryUrl);
        return repositoryRoot is null ? null : $"{repositoryRoot}/blob/HEAD/CONTRIBUTING.md";
    }

    /// <summary>
    /// Returns a URL pointing to the release notes or changelog for the package on GitHub,
    /// preferring the Releases page over a CHANGELOG.md link, or <c>null</c> if neither is available.
    /// </summary>
    private static string? TryGetReleaseHistoryUrl(PackageInfo package)
    {
        string? repositoryRoot = GitHubRepositoryUrl.TryGetWebRoot(package.RepositoryUrl);
        if (repositoryRoot is null)
        {
            return null;
        }

        if (package.HasReleaseNotes == true)
        {
            return $"{repositoryRoot}/releases";
        }

        if (package.HasChangelog == true && package.HasDefaultChangelog != true)
        {
            return $"{repositoryRoot}/blob/HEAD/CHANGELOG.md";
        }

        return null;
    }

    /// <summary>
    /// Returns a URL to the OpenSSF Scorecard viewer for the package's GitHub repository, or <c>null</c>
    /// if the repository URL is not a recognised GitHub URL.
    /// </summary>
    private static string? TryGetOpenSsfScorecardUrl(PackageInfo package)
    {
        string? repositoryRoot = GitHubRepositoryUrl.TryGetWebRoot(package.RepositoryUrl);
        if (repositoryRoot is null)
        {
            return null;
        }

        string repositoryIdentifier =
            repositoryRoot.Replace("https://github.com/", "github.com/", StringComparison.OrdinalIgnoreCase);

        return $"https://securityscorecards.dev/viewer/?uri={repositoryIdentifier}";
    }

    /// <summary>
    /// Returns a human-readable license string for display, noting when the license is missing,
    /// undetermined, or not one of PackageGuard's recognised SPDX identifiers.
    /// </summary>
    private static string FormatLicenseDisplay(PackageInfo package)
    {
        if (string.IsNullOrWhiteSpace(package.License) ||
            package.License.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "Missing or undetermined";
        }

        if (!IsWellKnownLicense(package.License))
        {
            return $"Present, but not one of PackageGuard's well-known license IDs: {package.License}";
        }

        return package.License;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="license"/> contains a substring matching one of PackageGuard's
    /// well-known SPDX license family markers.
    /// </summary>
    private static bool IsWellKnownLicense(string license)
    {
        string[] knownLicenseMarkers =
        [
            "MIT", "Apache", "BSD", "ISC", "Unlicense", "WTFPL", "CC0",
            "LGPL", "MPL",
            "GPL", "AGPL", "SSPL", "Commons Clause", "BUSL", "BCL"
        ];

        return knownLicenseMarkers.Any(marker =>
            license.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns a deduplicated, sorted array of display-friendly project paths referencing the given package.
    /// </summary>
    private static string[] GetDisplayProjectPaths(PackageInfo package)
    {
        return package.Projects
            .Select(path => ToDisplayProjectPath(package, path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Normalises a raw project path for display, appending <c>package.json</c> for npm packages
    /// that reference a directory rather than an explicit file.
    /// </summary>
    private static string ToDisplayProjectPath(PackageInfo package, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return string.Empty;
        }

        if (!package.Source.Equals("npm", StringComparison.OrdinalIgnoreCase))
        {
            return projectPath;
        }

        if (projectPath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
        {
            return projectPath;
        }

        return ChainablePath.From(projectPath) / "package.json";
    }
}
