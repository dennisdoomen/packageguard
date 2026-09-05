---
sidebar_position: 1
slug: /
---

# Introduction

**PackageGuard** is a fully open-source CLI tool that keeps your open-source supply chain honest. It scans the **NuGet, npm, pnpm and Yarn** dependencies of your codebase, enforces allow- and deny-lists for licenses, packages and versions, scores every package's legal/security/operational risk, and can emit a standards-compliant SBOM — all from a single, cacheable command that fits into any CI pipeline.

## What can it do?

At a glance, PackageGuard can:

- Scan **NuGet, npm, pnpm and Yarn** dependencies across an entire solution or codebase in one run, direct and transitive alike
- Enforce **allow- and deny-lists** for open-source licenses, specific packages, and package versions, discovered **hierarchically** across solution-, project- and repository-level configuration files
- Resolve **licenses** from NuGet/npm metadata, GitHub repositories, and downloaded license text through a chain of fetchers, falling back gracefully when a source doesn't have an answer
- Score every package's **risk** across three dimensions - Legal, Security and Operational - via `--report-risk`, weighing signals such as license compatibility, known vulnerabilities (OSV), maintainer activity, package signing, release cadence, and dozens more
- Back every risk score with **evidence, not just a number**: each package card in the HTML report has a dedicated Evidence section with collapsible, collapsed-by-default panels naming the exact packages, versions, GHSA/OSV vulnerability ids and release dates behind its rationale, so you can see *why* a package scored the way it did without digging through logs
- Produce a **colored console summary**, a **self-contained HTML report** you can open in a browser, and a **SARIF file** for surfacing violations and risk findings directly in GitHub code scanning
- Generate a standards-compliant **Software Bill of Materials (SBOM)** in **CycloneDX** or **SPDX** JSON format via `--sbom`, complete with purls, declared-vs-concluded license evidence, and a direct/transitive dependency graph
- Enrich that SBOM with **vulnerability data** from OSV when `--sbom` is combined with `--report-risk`
- **Cache** package, license and risk data (`--use-caching`) - including GitHub responses and per-repository risk profiles - to keep repeated scans and CI runs fast, with configurable cache freshness (`--risk-cache-max-age-hours`, `--refresh-risk-cache`)
- Run as a **.NET global tool** or a **portable, cross-platform** (Windows/Linux/macOS) deployment - no CI-specific plugin required

## What's so special about that?

I've noticed that the commercial solutions for this are usually very expensive and have functionality that smaller companies may not need. Hopefully this little tool fills the gap between tools like GitHub's Dependabot and expensive commercial products like Blackduck, SNYK and others.

## Who created this?

My name is Dennis Doomen and I'm a Microsoft MVP and Principal Consultant at [Aviva Solutions](https://avivasolutions.nl/) with 28 years of experience under my belt. As a software architect and/or lead developer, I specialize in designing full-stack enterprise solutions based on .NET as well as providing coaching on all aspects of designing, building, deploying and maintaining software systems. I'm the author of several open-source projects such as [Fluent Assertions](https://www.fluentassertions.com), [Reflectify](https://github.com/dennisdoomen/reflectify), [Liquid Projections](https://www.liquidprojections.net), and I've been maintaining [coding guidelines for C#](https://www.csharpcodingguidelines.com) since 2001.

Contact me through [Email](mailto:dennis.doomen@avivasolutions.nl), [Bluesky](https://bsky.app/profile/dennisdoomen.com), [Twitter/X](https://twitter.com/ddoomen) or [Mastodon](https://mastodon.social/@ddoomen).

## Where to next?

- [Installation](./installation.md) - install the global tool or the portable deployment
- [Configuration](./configuration.md) - define your allow- and deny-lists
- [Usage](./usage.md) - run a scan and read the results
- [Risk Metrics](./risk-metrics.md) - score packages on legal, security and operational risk
