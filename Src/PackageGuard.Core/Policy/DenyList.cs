using NuGet.Versioning;
using PackageGuard.Core.Common;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Policy;

public class DenyList : PackagePolicy
{
    /// <summary>
    /// Gets or sets a value indicating whether to deny all prerelease packages regardless of package name.
    /// </summary>
    public bool Prerelease { get; set; }

    /// <summary>
    /// Denies a package whose overall risk score (0-100) exceeds this value.
    /// </summary>
    public double? MaxOverallRisk { get; set; }

    /// <summary>
    /// Denies a package whose legal risk score (0-10) exceeds this value.
    /// </summary>
    public double? MaxLegalRisk { get; set; }

    /// <summary>
    /// Denies a package whose security risk score (0-10) exceeds this value.
    /// </summary>
    public double? MaxSecurityRisk { get; set; }

    /// <summary>
    /// Denies a package whose operational risk score (0-10) exceeds this value.
    /// </summary>
    public double? MaxOperationalRisk { get; set; }

    /// <summary>
    /// Denies a package whose highest known OSV vulnerability severity (CVSS score) exceeds this value.
    /// </summary>
    public double? MaxOsvSeverityScore { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to deny packages that are not signed. Packages for which signing
    /// is unknown (not evaluated, e.g. npm packages) are not denied.
    /// </summary>
    public bool DenyUnsigned { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to deny packages that are marked as deprecated by their registry.
    /// </summary>
    public bool DenyDeprecated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to deny packages that don't declare a repository URL.
    /// </summary>
    public bool DenyWithoutRepository { get; set; }

    /// <summary>
    /// Denies a package published more recently than the configured number of days, keyed by package ecosystem
    /// ("nuget" or "npm"). Packages without a known publish date are not denied.
    /// </summary>
    public Dictionary<string, int> MinPackageAgeDays { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether this deny list defines any rule that requires a package's risk signals
    /// (score, signing, deprecation, repository, or publish date) to have been collected beforehand.
    /// </summary>
    internal bool HasRiskPolicies =>
        MaxOverallRisk.HasValue || MaxLegalRisk.HasValue || MaxSecurityRisk.HasValue || MaxOperationalRisk.HasValue ||
        MaxOsvSeverityScore.HasValue || DenyUnsigned || DenyDeprecated || DenyWithoutRepository || MinPackageAgeDays.Count > 0;

    /// <summary>
    /// Determines if the given package is denied by the licenses or packages defined in this deny list.
    /// </summary>
    internal bool Denies(PackageInfo package) => EvaluateDeny(package).IsMatch;

    /// <summary>
    /// Evaluates the given package against this deny list and explains which rule, if any, denied it.
    /// </summary>
    internal PolicyDecision EvaluateDeny(PackageInfo package)
    {
        // Check if prerelease packages are denied
        if (Prerelease && NuGetVersion.Parse(package.Version).IsPrerelease)
        {
            return new PolicyDecision(true, "prerelease packages are denied by policy");
        }

        if (Licenses.Any())
        {
            string? matchingLicense = Licenses.FirstOrDefault(license =>
                license.Equals(package.License, StringComparison.OrdinalIgnoreCase));

            if (matchingLicense is not null)
            {
                LicenseSourceFiles.TryGetValue(matchingLicense, out string? licenseSourceFile);
                return new PolicyDecision(true, $"matches deny list license entry \"{matchingLicense}\"", licenseSourceFile);
            }
        }

        foreach (PackageSelector selector in Packages)
        {
            if (package.Name.MatchesWildcard(selector.Id) &&
                (selector.VersionRange is null || package.SatisfiesRange(package.Name, selector.VersionRange)))
            {
                return new PolicyDecision(true, $"matches deny list package entry \"{selector.Id}\"", selector.SourceFile);
            }
        }

        return new PolicyDecision(false, "no deny list rule matched");
    }

    /// <summary>
    /// Evaluates the given package against the risk-based rules in this deny list (risk score thresholds,
    /// OSV severity cap, signing/deprecation/repository checks, and minimum package age). Requires
    /// <paramref name="package"/> to have already been enriched and scored by the risk pipeline.
    /// </summary>
    internal PolicyDecision EvaluateRiskDeny(PackageInfo package)
    {
        if (MaxOverallRisk is { } maxOverallRisk && package.RiskScore > maxOverallRisk)
        {
            return new PolicyDecision(true,
                $"overall risk score {package.RiskScore:0.#} exceeds the maximum of {maxOverallRisk:0.#}");
        }

        if (MaxLegalRisk is { } maxLegalRisk && package.RiskDimensions.LegalRisk > maxLegalRisk)
        {
            return new PolicyDecision(true,
                $"legal risk score {package.RiskDimensions.LegalRisk:0.#} exceeds the maximum of {maxLegalRisk:0.#}");
        }

        if (MaxSecurityRisk is { } maxSecurityRisk && package.RiskDimensions.SecurityRisk > maxSecurityRisk)
        {
            return new PolicyDecision(true,
                $"security risk score {package.RiskDimensions.SecurityRisk:0.#} exceeds the maximum of {maxSecurityRisk:0.#}");
        }

        if (MaxOperationalRisk is { } maxOperationalRisk && package.RiskDimensions.OperationalRisk > maxOperationalRisk)
        {
            return new PolicyDecision(true,
                $"operational risk score {package.RiskDimensions.OperationalRisk:0.#} exceeds the maximum of {maxOperationalRisk:0.#}");
        }

        if (MaxOsvSeverityScore is { } maxOsvSeverityScore && package.MaxVulnerabilitySeverity > maxOsvSeverityScore)
        {
            return new PolicyDecision(true,
                $"highest known OSV severity {package.MaxVulnerabilitySeverity:0.#} exceeds the maximum of {maxOsvSeverityScore:0.#}");
        }

        if (DenyUnsigned && package.IsPackageSigned == false)
        {
            return new PolicyDecision(true, "package is not signed");
        }

        if (DenyDeprecated && package.IsDeprecated == true)
        {
            return new PolicyDecision(true, "package is deprecated");
        }

        if (DenyWithoutRepository && string.IsNullOrWhiteSpace(package.RepositoryUrl))
        {
            return new PolicyDecision(true, "package does not declare a repository URL");
        }

        if (MinPackageAgeDays.TryGetValue(package.GetPackageEcosystem(), out int minAgeDays) && package.PublishedAt is { } publishedAt)
        {
            double ageDays = (DateTimeOffset.UtcNow - publishedAt).TotalDays;
            if (ageDays < minAgeDays)
            {
                return new PolicyDecision(true,
                    $"published {ageDays:0.#} day(s) ago, which is less than the minimum age of {minAgeDays} day(s)");
            }
        }

        return new PolicyDecision(false, "no risk-based deny list rule matched");
    }
}
