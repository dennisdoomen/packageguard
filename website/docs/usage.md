---
sidebar_label: Running a scan
---

# Running a scan

A scan checks every direct and transitive package in your codebase against your [policy](./configuration.md):

```bash
packageguard <path>
```

This is the `analyze` command. `packageguard analyze <path>` does the same.

## What to scan

The path is optional and defaults to the current folder. It can be:

- a folder with a `.sln` or `.slnx` file, a `package.json` file, or both
- a specific `.sln`, `.slnx` or `.csproj` file
- a specific `package.json` file

In one run, PackageGuard scans:

- **NuGet** - every project of the solution. Use `--nuget false` to skip NuGet.
- **npm, pnpm or Yarn** - one `package.json` project. PackageGuard detects the package manager from the lock file. Use `--npm npm|yarn|pnpm` to choose one, or `--npm None` to skip npm scanning. If the `package.json` is not in the same folder as the solution, point to it with `--npm-path`.

PackageGuard finds your [configuration files](./configuration.md#where-packageguard-looks-for-configuration) automatically. You only need `--config-path` to use a specific file.

## Prerequisites and restore

PackageGuard reads the packages from what your package manager already resolved:

- For NuGet, that is the `project.assets.json` file that `dotnet restore` writes. If it is missing or out of date, PackageGuard runs `dotnet restore` first. Use `--force-restore` to always restore, `--skip-restore` to never restore, and `--restore-interactive false` in CI so that a restore never waits for a sign-in prompt.
- For npm, pnpm and Yarn, that is the lock file. If there is no lock file, PackageGuard asks the package manager to create one. The package manager must be on the `PATH`, or you pass its location with `--npm-exe-path`.

PackageGuard uses the same feeds and credential providers as `dotnet` and your package manager, so private feeds work without extra setup.

## NuGet source warnings

PackageGuard checks each project's effective NuGet configuration before restore. It warns when nuget.org and another enabled feed can supply the same package IDs because there is no package source mapping or because both feeds have the same highest-priority mapping. The warning names the feeds and configuration files.

Configure [NuGet package source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping) to assign private package IDs to their intended feed. A more specific private mapping takes precedence over a broad nuget.org `*` mapping and does not cause a warning.

The check is offline and cannot tell which packages or feeds are private. It does not account for sources supplied by MSBuild properties or restore arguments. It also cannot verify where packages already in the global package cache came from. It runs with `--skip-restore` and when PackageGuard ignores a feed for metadata, because NuGet may still use that feed during restore.

To fail the scan on these warnings, set [`failOnNugetSourceWarnings`](./configuration.md#nuget-source-warnings) in PackageGuard's configuration.

## Reading the output

If everything is allowed, you get something like:

![Console output of a PackageGuard scan](/img/console-output.png)

Each violation looks like this:

```
SomePackage 2.1.0 [DENIED]
- License: GPL-3.0-only
- Feed: nuget.org (https://api.nuget.org/v3/index.json)
- Reason: ...
- Resolve: ...
- Projects:
  - C:\Workspaces\MyApp\MyApp.csproj
```

- The label is `DENIED` for a violation that fails the build, `WARNING` for a match in the [`warn` section](./configuration.md#warnings-instead-of-build-failures), and `MALICIOUS` for a [known malicious package](./risk-metrics.md#malicious-packages).
- `Reason` names the rule that the package broke.
- `Resolve` suggests how to fix it, for example by upgrading, or by adding an exception to your configuration.
- `Projects` lists the projects that use the package.

To see *why* a package is in your project, and which direct dependency brings it in, use [`packageguard explain`](./explain.md).

## Exit codes

| Exit code | Meaning |
| --- | --- |
| `0` | No violations, or only warnings. |
| `1` | A package was denied, a known malicious package was found, or a NuGet source warning was configured to fail the scan. |

Two options change this:

- `--treat-deny-as-warning` (or the `PACKAGEGUARD_DENY_AS_WARNING` environment variable) reports denied packages as warnings, so the run passes. Malicious packages still fail it. This helps when you adopt a new policy step by step.
- `--ignore-violations` always exits with `0`.

## More options

- `--report-risk` - score every package on risk and write an HTML and SARIF report. See [Risk reports](./risk-metrics.md).
- `--sbom` and `--sbom-output` - write a Software Bill of Materials. See [Software Bill of Materials](./sbom.md).
- `--use-caching` and `--github-api-key` - make runs faster and avoid GitHub rate limits. See [Caching and rate limits](./caching-and-rate-limits.md).
- `--verbose` - show debug output, including the individual HTTP calls when combined with `--report-risk`.

The [CLI reference](./cli-reference.md) lists every option.
