using PackageGuard.Core.Package;
using PackageGuard.Core.Policy;

namespace PackageGuard.Core.Risk.Scoring;

/// <summary>
/// Evaluates legal risk based on license information and compliance factors.
/// </summary>
internal sealed class LegalRiskEvaluator : IEvaluateRiskDimension
{
    /// <inheritdoc/>
    public RiskDimensionEvaluation Evaluate(PackageInfo package)
    {
        var risk = 0.0;
        var rationale = new List<string>();

        if (string.IsNullOrEmpty(package.License))
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "No license information found", 6.0);
        }
        else if (package.License.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale,
                $"Non-standard or unrecognized license type ({package.License})", 6.0);
        }
        else
        {
            // With OR the consumer may pick any option, so the least risky one counts. With AND all licenses apply.
            var (alternativeRisk, label) = LicenseExpression.Parse(package.License).Evaluate(
                term => Classify(term.Id),
                (left, right) => left.Risk <= right.Risk ? left : right,
                (left, right) => left.Risk >= right.Risk ? left : right);

            risk += RiskEvaluationHelpers.AddContribution(rationale, $"{label} ({package.License})", alternativeRisk);
        }

        // A license that the package declares itself (for example an SPDX expression in the nuspec) needs no URL
        bool urlIsRequired = package.LicenseEvidence != LicenseEvidence.Declared;

        if (urlIsRequired && (package.HasValidLicenseUrl is false || string.IsNullOrEmpty(package.LicenseUrl)))
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "Missing or invalid license URL", 1.0);
        }
        else if (package.HasValidLicenseUrl is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("License URL", 0.0));
        }

        if (package.IsLicensePolicyCompatible is false)
        {
            risk += RiskEvaluationHelpers.AddContribution(rationale, "License is incompatible with configured policy", 3.0);
        }
        else if (package.IsLicensePolicyCompatible is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("License matches configured policy", 0.0));
        }

        return RiskEvaluationHelpers.CreateEvaluation(risk, rationale);
    }

    // Weak copyleft goes first, because "LGPL" contains "GPL"
    private static (double Risk, string Label) Classify(string license) =>
        IsWeakCopyleftLicense(license) ? (3.0, "Weak copyleft license") :
        IsRestrictiveLicense(license) ? (6.0, "Restrictive license") :
        IsPermissiveLicense(license) ? (0.0, "Permissive license") :
        (0.0, "Unclassified license");

    /// <summary>
    /// Returns <see langword="true"/> if the license is considered restrictive (copyleft or commercial restrictions).
    /// </summary>
    private static bool IsRestrictiveLicense(string license)
    {
        string[] restrictiveLicenses = ["GPL", "AGPL", "SSPL", "Commons Clause", "BUSL", "BCL"];
        return restrictiveLicenses.Any(r => license.Contains(r, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns <see langword="true"/> if the license is a weak copyleft license (e.g. LGPL, MPL).
    /// </summary>
    private static bool IsWeakCopyleftLicense(string license)
    {
        string[] weakCopyleftLicenses = ["LGPL", "MPL"];
        return weakCopyleftLicenses.Any(c => license.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns <see langword="true"/> if the license is considered permissive (MIT, Apache, BSD, etc.).
    /// </summary>
    private static bool IsPermissiveLicense(string license)
    {
        string[] permissiveLicenses = ["MIT", "Apache", "BSD", "ISC", "Unlicense", "WTFPL", "CC0"];
        return permissiveLicenses.Any(p => license.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}
