namespace PackageGuard.Core;

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
            risk += 6.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("No license information found", 6.0));
        }
        else if (package.License.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            risk += 6.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Non-standard or unrecognized license type ({package.License})", 6.0));
        }
        else if (IsRestrictiveLicense(package.License))
        {
            risk += 6.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Restrictive license ({package.License})", 6.0));
        }
        else if (IsWeakCopyleftLicense(package.License))
        {
            risk += 3.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Weak copyleft license ({package.License})", 3.0));
        }
        else if (IsPermissiveLicense(package.License))
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Permissive license ({package.License})", 0.0));
        }
        else
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale($"Unclassified license ({package.License})", 0.0));
        }

        if (package.HasValidLicenseUrl is false || string.IsNullOrEmpty(package.LicenseUrl))
        {
            risk += 1.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Missing or invalid license URL", 1.0));
        }
        else if (package.HasValidLicenseUrl is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("Valid license URL", 0.0));
        }

        if (package.IsLicensePolicyCompatible is false)
        {
            risk += 3.0;
            rationale.Add(RiskEvaluationHelpers.CreateRationale("License is incompatible with configured policy", 3.0));
        }
        else if (package.IsLicensePolicyCompatible is true)
        {
            rationale.Add(RiskEvaluationHelpers.CreateRationale("License matches configured policy", 0.0));
        }

        return RiskEvaluationHelpers.CreateEvaluation(risk, rationale);
    }

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
