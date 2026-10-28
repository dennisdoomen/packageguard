---
sidebar_label: Risk signals
---

# Risk signals

This page lists every signal that goes into the [risk score](./risk-metrics.md). PackageGuard combines package metadata, repository evidence, CI workflow signals, dependency-graph data, signing checks and OSV vulnerability data.

Not every package exposes every signal. PackageGuard uses the evidence it can find. Missing or weak signals are scored conservatively, so they count as risk instead of being ignored.

## Legal risk

- **License presence** - missing or unknown licenses are treated as high legal risk.
- **License type** - restrictive licenses (for example GPL, AGPL, SSPL or BUSL-style terms) increase risk more than permissive licenses such as MIT, BSD or Apache-2.0.
- **Weak copyleft detection** - licenses such as LGPL or MPL score lower than strong copyleft, but higher than permissive licenses.
- **License expressions** - for an SPDX expression, the least restrictive option of an `OR` counts (you can choose), and the most restrictive license of an `AND` counts (all of them apply).
- **License URL validity** - missing or invalid license URLs add a small amount of legal risk, because they make the package harder to audit.
- **Policy compatibility** - if the detected license conflicts with your configured allow and deny lists, legal risk goes up further.

## Security risk

- **Repository availability** - packages without a public source repository are riskier, because the code and maintenance practices are harder to inspect.
- **Direct vulnerability count** - known vulnerabilities in the package itself.
- **Transitive vulnerability count** - vulnerable dependencies beneath the package.
- **Maximum vulnerability severity** - a few severe findings can outweigh many low-severity ones.
- **Exploit data** - a vulnerability on the CISA KEV list adds a heavy penalty (+3.0). The EPSS probability of exploitation weighs vulnerabilities of the same severity (up to +1.5).
- **Recent vulnerability fix activity** - recent security fixes can mean active vulnerability handling, but also recent exposure.
- **Available fixes** - whether a fixed version of a known vulnerability already exists.
- **Median time to fix vulnerabilities** - slow fix times mean a weaker security response.
- **Dependency depth** - deeper dependency chains increase the attack surface.
- **Pre-1.0 dependencies** - depending on unstable pre-1.0 packages adds some supply-chain risk.
- **Package signing** - unsigned packages, or packages that fail trust verification, are riskier.
- **Trusted package signature** - a valid, trusted signature lowers the risk.
- **Verified publisher signal** - packages with a stronger publisher trust signal score better.
- **Verified release signature signal** - signed, verifiable releases lower the risk of tampering.
- **Verified commit coverage** - repositories where most recent commits are verified score better.
- **Native or binary assets** - packages that ship native or binary content are harder to audit, so they get extra scrutiny.
- **Deprecation status** - deprecated packages increase security concern.
- **Abandoned transitive dependencies** - transitive dependencies that are stale and also have a vulnerability or low maintainer activity add risk.
- **Unmaintained critical transitives** - critical transitive dependencies that are stale and have a high-severity vulnerability are a stronger risk signal.
- **Maintainer concentration on a non-organization account** - a package maintained by one person on a personal account is more fragile.
- **Owner account age** - very new repository owner accounts get a small trust penalty.
- **Security policy presence and quality** - repositories with a clear `SECURITY.md` and concrete reporting guidance score better.
- **Coordinated disclosure guidance** - explicit disclosure instructions lower the uncertainty when vulnerabilities are found.
- **Provenance and attestation signals** - build provenance or artifact attestation workflows lower the risk of tampering.
- **Reproducible build signals** - evidence of deterministic or reproducible builds improves supply-chain confidence.

## Operational risk

- **Release recency** - packages whose latest release is old are more likely to be stale.
- **Mean release interval** - long average gaps between releases increase risk. A regular release cadence lowers it.
- **Prerelease ratio** - repositories with mostly prereleases may be unstable.
- **Rapid release correction count** - many quick follow-up releases can point to problems with release quality.
- **Release notes coverage** - repositories with real release notes or changelog entries are easier to upgrade safely.
- **Semantic versioning discipline** - release tags that don't look like SemVer make the impact of an upgrade harder to predict.
- **Major release ratio** - a high share of major releases can mean frequent breaking changes.
- **README presence and quality** - missing or boilerplate README files increase risk.
- **README freshness** - a stale README is a sign of weaker project hygiene.
- **CHANGELOG presence and quality** - missing or low-quality changelogs make upgrades riskier.
- **CHANGELOG freshness** - an outdated change history adds uncertainty.
- **CONTRIBUTING guidance** - projects with clear contribution guidance are easier for others to help maintain.
- **Contributor count** - very few contributors means a higher risk if one of them leaves.
- **Recent maintainer count** - few recently active maintainers means ownership is concentrated.
- **Top contributor concentration** - when one or two people do most of the work, the project is less resilient.
- **Median maintainer inactivity** - long gaps since the last maintainer activity suggest the project is losing its maintainers.
- **External contribution rate** - a healthy rate of outside contributions suggests the community can help maintain the project.
- **Reviewer diversity** - more distinct reviewers improve oversight of changes.
- **Open bug count** - many open bugs increase risk.
- **Stale critical bug count** - old, unresolved critical bugs are a stronger signal.
- **Median open bug age** - long-lived bugs suggest bugs are not handled quickly.
- **Bug closure rate** - closing few bugs compared to new or reopened ones increases risk.
- **Bug reopen rate** - a high reopen rate can mean low fix quality.
- **Median issue response time** - long first-response times suggest maintainers respond slowly.
- **Median critical issue response time** - slow responses on critical issues are penalized more.
- **Issue response coverage** - if maintainers respond to only a small part of the issues, risk increases.
- **Issue triage within seven days** - slow early triage is a weaker, but still useful, health signal.
- **Median pull request merge time** - long merge times can mean a shortage of reviewers or maintainers.
- **Recent successful CI runs** - repositories without recent successful workflow runs are less healthy.
- **Workflow failure rate** - frequent CI failures increase risk.
- **Flaky workflow patterns** - inconsistent pass and fail history is a reliability signal.
- **Required status checks** - protected branches with required checks score better.
- **Workflow platform breadth** - CI that runs on more than one platform gives more confidence.
- **Coverage workflow signal** - explicit code coverage reporting is a positive signal.
- **Test execution signal** - repositories with clear automated test workflows score better.
- **Dependency update automation** - tools like Dependabot or Renovate keep dependencies up to date.
- **Stale transitive dependencies** - old or stale dependencies in the tree add risk.
- **Deprecated transitive dependencies** - transitive dependencies that are marked as deprecated add maintenance concern.
- **Package popularity** - very low download counts are a weaker ecosystem signal.
- **Latest stable version tracking** - if the version you use is behind the latest stable release, the package is flagged as lagging.
- **Version update lag** - the further behind the latest stable version, the higher the risk.
- **Target framework freshness** - packages that only target old frameworks score worse than packages that support modern runtimes.
- **OpenSSF Scorecard** - a low [Scorecard](https://scorecard.dev/) score increases risk. A strong score lowers it.
- **Branch protection** - repositories without branch protection on the default branch are riskier.
- **Repository ownership or rename churn** - recent ownership transfers or renames can mean instability, or make it unclear where the package comes from.
