---
sidebar_position: 1
slug: /
---

# Introduction

**PackageGuard** is a fully open-source tool to scan the NuGet, NPM, PNPM and Yarn dependencies of your codebase against a deny- or allowlist, so you can control the open-source licenses that you want to allow, or certain versions of certain packages you want to enforce or avoid.

## What can it do?

At a glance, PackageGuard can:

- Scan **NuGet, NPM, PNPM and Yarn** dependencies across your entire solution or codebase
- Enforce **allow- and deny-lists** for open-source licenses, specific packages, and package versions
- Discover configuration **hierarchically**, merging solution-, project-, and repository-level policies
- Resolve **licenses** from NuGet/npm metadata, GitHub repositories, and other sources through a chain of fetchers
- Assess **risk** for every package across legal, security, and operational dimensions (e.g. vulnerabilities, maintenance activity, signing) via `--report-risk`
- Generate a **colored console summary**, a self-contained **HTML report**, and a **SARIF file** for CI integration
- Produce a standards-compliant **Software Bill of Materials (SBOM)** in **CycloneDX** or **SPDX** JSON format via `--sbom`
- Include **vulnerability data** (from OSV) in the SBOM when combined with `--report-risk`
- **Cache** package, license, and risk data (`--use-caching`) to speed up repeated scans, with configurable cache freshness
- Run as a **.NET global tool** or a **portable, cross-platform** (Windows/Linux/macOS) deployment

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
