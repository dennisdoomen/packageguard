---
sidebar_label: Quick start
---

# Quick start

This page takes you from nothing to a working license policy in five steps. Run the commands from the folder that contains your `.sln`, `.slnx` or `package.json` file.

## 1. Install PackageGuard

```bash
dotnet tool install PackageGuard --global
```

See [Installation](./installation.md) for the prerequisites and the portable version.

## 2. Create a policy

```bash
packageguard init
```

`init` scans your dependencies, shows which licenses they use, and asks what kind of software you build: proprietary, SaaS or open source. It then writes `.packageguard/config.json` with a list of allowed licenses that fits your answer. See [Creating a policy with `init`](./init.md).

You can skip this step. Without a configuration file, PackageGuard only allows well-known permissive licenses, such as MIT, Apache-2.0 and the BSD licenses.

## 3. Run a scan

```bash
packageguard .
```

PackageGuard restores your projects if needed, finds the license of every direct and transitive package, and checks each one against your policy. It exits with code `0` when everything is allowed, and `1` when a package is denied. See [Running a scan](./usage.md) for how to read the output.

## 4. Fix a violation

Every denied package shows a `Reason` line with the rule it broke, and a `Resolve` line with what you can do about it. If you don't know why a package is in your project, ask PackageGuard:

```bash
packageguard explain <package-name>
```

This shows which of your direct dependencies brings the package in, and which rule in which configuration file denied it. See [Explaining a package](./explain.md).

Then either remove or replace the dependency, or allow its license or the package itself in your [configuration file](./configuration.md).

## 5. Look at the risk

A license check tells you whether you *may* use a package. A risk report tells you whether you *should*. It scores every package on legal, security and operational risk, and lists known vulnerabilities.

Risk scoring needs a lot of GitHub API calls. Create a GitHub personal access token and make it available first:

```bash
export GITHUB_API_KEY=<your-token>
```

Then run:

```bash
packageguard . --report-risk --use-caching
```

PackageGuard prints a summary and writes an HTML report you can open in a browser. `--use-caching` keeps the collected data, so the next run is much faster. See [Risk reports](./risk-metrics.md) and [Caching and rate limits](./caching-and-rate-limits.md).

## Next steps

- [Run PackageGuard in your CI pipeline](./ci-integration.md)
- [Fail the build on risk, vulnerabilities or package age](./risk-gates.md)
- [Find redundant package references and version conflicts](./dependencies.md)
- [Generate a Software Bill of Materials](./sbom.md)
