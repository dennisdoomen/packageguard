---
sidebar_label: Introduction
slug: /
---

# Introduction

**PackageGuard** is an open-source command-line tool that checks the open-source packages your code depends on. It scans the **NuGet, npm, pnpm and Yarn** dependencies of your codebase, checks their licenses against your policy, and scores every package on legal, security and operational risk. It runs in one command, on your machine or in any CI pipeline, without an account.

![HTML risk report of PackageGuard](/img/risk-report.png)

## What can it do?

- **Enforce a license and package policy.** Allow or deny licenses (including SPDX expressions such as `MIT OR Apache-2.0`), packages and versions, for direct and transitive dependencies alike. Put a policy next to your solution, and refine it per project. See [Configuration file](./configuration.md).
- **Get you started.** `packageguard init` looks at the licenses you actually use and writes a policy that fits. See [Creating a policy with `init`](./init.md).
- **Score risk.** Rate every package from 0 to 100, based on known vulnerabilities, exploit data, signing, maintainer activity, release cadence and dozens of other signals. Get a console summary, an HTML report and a SARIF file for GitHub code scanning. See [Risk reports](./risk-metrics.md).
- **Block risky packages.** Fail the build on a risk threshold, a severe or actively exploited vulnerability, or a package version that was published only hours ago. Known malicious packages always fail the build. See [Risk gates and exceptions](./risk-gates.md).
- **Explain a package.** `packageguard explain` shows why a package is in your project, which rule allowed or denied it, and its risk. See [Explaining a package](./explain.md).
- **Clean up references.** `packageguard dependencies` finds package references you don't need, and packages that resolve to different versions in different projects. See [Redundant references and version conflicts](./dependencies.md).
- **Generate an SBOM.** Write a Software Bill of Materials in CycloneDX or SPDX format. See [Software Bill of Materials](./sbom.md).

## Why PackageGuard?

Commercial tools for this are usually expensive, and have features that smaller companies may not need. PackageGuard tries to fill the gap between free tools like GitHub's Dependabot and commercial products like Black Duck and Snyk.

## Where to start

1. [Install PackageGuard](./installation.md)
2. Follow the [Quick start](./quick-start.md) to create a policy and run your first scan
3. [Add it to your CI pipeline](./ci-integration.md)

## Who created this?

My name is Dennis Doomen and I'm a Microsoft MVP and Principal Consultant at [Aviva Solutions](https://avivasolutions.nl/) with 28 years of experience under my belt. As a software architect and/or lead developer, I specialize in designing full-stack enterprise solutions based on .NET as well as providing coaching on all aspects of designing, building, deploying and maintaining software systems. I'm the author of several open-source projects such as [Fluent Assertions](https://www.fluentassertions.com), [Reflectify](https://github.com/dennisdoomen/reflectify), [Liquid Projections](https://www.liquidprojections.net), and I've been maintaining [coding guidelines for C#](https://www.csharpcodingguidelines.com) since 2001.

Contact me through [Email](mailto:dennis.doomen@avivasolutions.nl), [Bluesky](https://bsky.app/profile/dennisdoomen.com), [Twitter/X](https://twitter.com/ddoomen) or [Mastodon](https://mastodon.social/@ddoomen).
