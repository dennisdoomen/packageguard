---
sidebar_label: Risk reports
---

# Risk reports

PackageGuard can score every package in your project on legal, security and operational risk. Add `--report-risk` to a scan to get a risk summary in the console, a detailed HTML report and a SARIF file:

```bash
packageguard --report-risk <path-to-project>
```

Risk scoring reads a lot of data from GitHub. Without a GitHub API key you will run into GitHub's rate limit on anything but a very small project. See [Caching and rate limits](./caching-and-rate-limits.md) to set up a key and to make repeated runs fast.

## How the score works

Each package gets three scores from `0` to `10`, one per dimension. The overall score is a weighted total, scaled to `0`-`100`:

- **Legal Risk** (`20%`) - the license, and whether it fits your policy
- **Security Risk** (`45%`) - vulnerabilities, signing, provenance and other supply-chain signals
- **Operational Risk** (`35%`) - maintenance activity, release cadence, CI health and similar signals

The overall score falls in one of three zones:

- 🟢 Green (`0.0` - `29.9`) - low risk
- 🟡 Yellow (`30.0` - `59.9`) - medium risk
- 🔴 Red (`60.0` - `100.0`) - high risk

[Risk signals](./risk-signals.md) lists every signal that goes into each dimension.

Risk scores aren't only for reporting. Your configuration can fail the build when a package is above a risk threshold, too new, or has a severe vulnerability. See [Risk gates and exceptions](./risk-gates.md).

## The console summary

The console lists every package with its overall score, followed by the paths of the HTML and SARIF reports:

```
Package Risk Summary:

- Glob 1.1.9: 42.8/100 (Medium)
- Azure.Core 1.44.1: 35.8/100 (Medium)
- FluentAssertions 8.8.0: 28.5/100 (Low)

Detailed risk report:
C:\Users\<you>\AppData\Local\Temp\PackageGuard\reports\mockly-risk-report-20260325-045014.html
C:\Users\<you>\AppData\Local\Temp\PackageGuard\reports\mockly-risk-report-20260325-045014.sarif
```

By default, the reports go to a `PackageGuard/reports` folder in your temporary folder. Pass a folder or file name to `--report-risk`, or set the `PACKAGEGUARD_REPORT_DIRECTORY` environment variable, to choose where they go. A folder gets generated file names. An explicit file name is used as-is, and may overwrite an earlier report.

The HTML report is for people. The SARIF report is for CI systems. You can upload it to GitHub code scanning, or keep it as a build artifact next to the HTML report. See [CI integration](./ci-integration.md).

## The HTML report

![HTML risk report of PackageGuard](/img/risk-report.png)

The HTML report is a single static file, with no scripts and no external assets. From top to bottom, it contains:

- The project path and the time the report was generated.
- A red block with any [known malicious packages](#malicious-packages), when there are any.
- Three blocks with the number of packages and the overall status, the critical (CISA KEV) and total vulnerability counts, and the number of packages per risk zone. Links jump to the vulnerabilities, the dependency hygiene section and the package details.
- A **Vulnerabilities** table. See [Vulnerabilities](#vulnerabilities).
- A package summary table with clickable package names and a Vulnerabilities column. Vulnerable packages are listed first and highlighted in red.
- A **Dependency Hygiene** section. See [Dependency hygiene](#dependency-hygiene).
- A details card for every package.

Each package card shows the overall score with the vulnerability count next to it, the legal, security and operational scores, and the rationale behind them:

![Package details in the HTML risk report](/img/risk-report-package.png)

Below the rationale, the **Evidence** section shows the data behind the score, grouped into Package, Security, Maintenance and Releases cards. It names the exact packages, versions, OSV vulnerability IDs and release dates behind findings about vulnerable, stale, abandoned, deprecated and unmaintained transitive dependencies. These lists are collapsed by default, so long lists don't clutter the page.

## Vulnerabilities

Known vulnerabilities come from [OSV](https://osv.dev) and drive most of the security score. The HTML report shows separate indicators, so you can tell a package that is vulnerable itself from one that only depends on a vulnerable package:

- **Direct vulnerabilities** - the number of advisories that affect the package version you use, with the highest severity. The advisory IDs are listed in the package's Evidence section.
- **Transitive vulnerabilities** - advisories in the dependencies beneath the package. The Evidence section names each vulnerable dependency, its version and the advisory IDs.
- **Fix availability** - whether a fixed version already exists, so you know if upgrading solves the problem.
- **Fix speed** - the median time between the publication of an advisory and the update that fixes it.

The **Vulnerabilities** table lists every vulnerability, with the CISA KEV ones first. The row colors mean:

- red - the vulnerability is on the CISA KEV list
- yellow - the vulnerability applies
- green - you marked it as not applicable with a [vulnerability exception](./risk-gates.md#vulnerability-exceptions)

### Exploit data

For every CVE alias of an OSV finding, PackageGuard looks up:

- the [EPSS](https://www.first.org/epss/) probability that it will be exploited within 30 days
- whether it is on the [CISA Known Exploited Vulnerabilities (KEV)](https://www.cisa.gov/known-exploited-vulnerabilities-catalog) list

A KEV vulnerability adds a heavy penalty (+3.0) to the Security score. The EPSS score weighs vulnerabilities of the same severity (up to +1.5). Both values appear in the HTML report and in the SARIF properties. If either source can't be reached, PackageGuard logs a warning and continues without that data.

### Malicious packages

An OSV advisory whose ID starts with `MAL-` comes from the OpenSSF Malicious Packages data. PackageGuard does not treat it as a normal vulnerability. Whenever risk data is collected, a package with such an advisory always fails the run. Its risk score, your policy and your risk exceptions don't change that, and `--treat-deny-as-warning` doesn't turn it into a warning.

A malicious package is listed first in the console output, in its own block at the top of the HTML report, and as a `packageguard/malicious-package` result at SARIF level `error`.

## Dependency hygiene

Dependency hygiene is about the *shape* of your dependency graph, not the quality of individual packages. When the report covers NuGet projects, the HTML report has a **Dependency Hygiene** section that lists:

- **Redundant references** - a direct `PackageReference` that a referenced project or another direct package already provides.
- **Version conflicts** - one package that resolves to different versions in different projects.

These are the same findings that the [`dependencies` command](./dependencies.md) reports. The data comes from the restore output that PackageGuard already loaded for the scan, so it adds no extra restore and no network access.

Hygiene findings are not part of the risk score. They never change a package's score, the risk zone counts, the overall report status or the exit code. The SARIF report does not include them.
