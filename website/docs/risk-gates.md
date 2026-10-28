---
sidebar_label: Risk gates and exceptions
---

# Risk gates and exceptions

Besides package identity and license, the `deny` section of your [configuration file](./configuration.md) can fail the build on the [risk score](./risk-metrics.md) and other signals that PackageGuard collects for a package.

## Gating on risk and package age

```json
{
    "settings": {
        "deny": {
            "maxOverallRisk": 60,
            "maxLegalRisk": 5,
            "maxSecurityRisk": 7,
            "maxOperationalRisk": 8,
            "maxOsvSeverityScore": 7.0,
            "denyKnownExploited": true,
            "denyUnsigned": true,
            "denyDeprecated": true,
            "denyWithoutRepository": true,
            "minPackageAgeDays": {
                "npm": 14,
                "nuget": 3
            }
        }
    }
}
```

- `maxOverallRisk` - denies a package whose overall risk score (`0`-`100`) is higher than this value.
- `maxLegalRisk`, `maxSecurityRisk`, `maxOperationalRisk` - deny a package whose legal, security or operational score (`0`-`10` each) is higher than this value. A package can score very badly on one dimension and still stay under `maxOverallRisk`. These settings give you more targeted control than the overall score.
- `maxOsvSeverityScore` - denies a package whose highest known OSV/CVSS severity is higher than this value. For example, `7.0` denies anything rated "High" or above, whatever the overall security score is.
- `denyKnownExploited` - denies a package that has a vulnerability on the [CISA Known Exploited Vulnerabilities (KEV)](https://www.cisa.gov/known-exploited-vulnerabilities-catalog) list.
- `denyUnsigned` - denies packages that aren't signed. Packages for which signing can't be checked (for example npm packages) are never denied by this rule.
- `denyDeprecated` - denies packages that their registry marks as deprecated.
- `denyWithoutRepository` - denies packages that don't declare a repository URL.
- `minPackageAgeDays` - denies a package version published fewer than the given number of days ago, per ecosystem (`npm` or `nuget`). This is a cheap defense against a common attack: someone takes over a maintainer account, publishes a malicious version, and it is removed again within hours.

Setting any of these rules makes PackageGuard collect risk data for that project, even without `--report-risk`. Without that data there is nothing to check the rules against. Collecting risk data makes a run slower, and works best with a [GitHub API key](./caching-and-rate-limits.md#github-rate-limits).

A build can pass today and fail tomorrow, because a new CVE was published or a risk score changed. That is expected when you gate on live risk data. Use [risk exceptions](#risk-exceptions) for packages whose risk you have accepted.

Known malicious packages (OSV `MAL-` advisories) always fail the run whenever risk data is collected. You don't need a rule for that. See [Risk reports](./risk-metrics.md#malicious-packages).

## Risk exceptions

`riskExceptions` excludes a package from the risk-based `deny` rules above, even if it is above a threshold or has a known vulnerability. You can limit an exception to a version or version range, and give it an expiry date. It has no effect on the ordinary package and license rules.

```json
{
    "settings": {
        "riskExceptions": [
            {
                "package": "left-pad",
                "reason": "Vetted manually on 2026-01-15, won't-fix CVE-2025-12345 does not apply to our usage",
                "expiresOn": "2026-07-01"
            },
            {
                "package": "some-native-lib",
                "versions": "[1.0.0,2.0.0)",
                "reason": "Signing certificate expired but publisher identity verified out-of-band"
            }
        ]
    }
}
```

After the `expiresOn` date, the exception no longer applies. The package is denied again if it still breaks a risk rule.

For every denied package, the output shows a `Resolve` line after the `Reason` line. It names the fix for that kind of rule. For a risk rule, it points to `riskExceptions`:

```
System.Security.Cryptography.Pkcs 10.0.13 [DENIED]
- License: MIT
- Feed: nuget.org (https://api.nuget.org/v3/index.json)
- Reason: published 1.5 day(s) ago, which is less than the minimum age of 3 day(s)
- Resolve: Use an older version, wait until this version is 3 day(s) old, or add it to the riskExceptions section of your config. Set expiresOn to make the exception temporary
- Projects:
  - C:\Workspaces\MyApp\MyApp.csproj
```

A license violation never suggests `riskExceptions`, because a risk exception does not change license rules. A match in the `warn` section has no `Resolve` line.

## Vulnerability exceptions

`vulnerabilityExceptions` records that you analyzed a specific vulnerability and decided it does not apply to you. It needs a `reason`, and a `reviewOn` date on which you must look at the decision again.

```json
{
    "settings": {
        "vulnerabilityExceptions": [
            {
                "vulnerability": "CVE-2024-0001",
                "package": "System.Security.Cryptography.Xml",
                "reason": "We only parse XML that we generate ourselves, so the vulnerable path is never reached.",
                "reviewOn": "2026-12-01"
            }
        ]
    }
}
```

- `vulnerability` - the OSV identifier or any of its aliases, such as a `CVE-` or `GHSA-` ID. An advisory ID already identifies the affected versions, so there is no version setting.
- `package` - optional name or wildcard. Use it when the same vulnerability affects several packages and you only analyzed one.
- `reason` and `reviewOn` - both are required.

Until the review date, the vulnerability does not count. It is left out of the vulnerability totals, the Security score and the deny rules (`maxOsvSeverityScore`, `denyKnownExploited`). The HTML report still lists it in the Vulnerabilities table, on a green row with the reason and review date.

After the review date, the exception no longer applies, the vulnerability counts again, and the report shows that the decision expired.

When several projects use the same package, the policy of each of those projects needs the exception. A `MAL-` malicious-package advisory is only covered when you name its exact ID.
