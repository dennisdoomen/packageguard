---
sidebar_label: Installation
---

# Installation

PackageGuard runs on Windows, Linux and macOS, and needs the .NET 9.0 or 10.0 runtime. You can install it in two ways.

## .NET global tool (recommended)

Install it as [a NuGet tool package](https://www.nuget.org/packages/packageguard):

```bash
dotnet tool install PackageGuard --global
```

Then check that it works:

```bash
packageguard --help
```

To update it later, run `dotnet tool update PackageGuard --global`.

## Portable version

Download the latest `PackageGuard-{version}.zip` from the [releases page](https://github.com/dennisdoomen/packageguard/releases/latest) and extract it. Run it with:

```bash
dotnet PackageGuard.dll --help
```

## What else you need

PackageGuard reads the packages that your package manager already resolved, so it needs the same tools you use to build:

- **NuGet projects** need the .NET SDK, because PackageGuard runs `dotnet restore` when `project.assets.json` is missing or out of date.
- **npm, pnpm and Yarn projects** need that package manager on the `PATH`, or its location passed with `--npm-exe-path`. PackageGuard uses it to create the lock file when there is none.
- **License lookups and risk reports** call the GitHub API. Get a GitHub API key to avoid its rate limits. See [Caching and rate limits](./caching-and-rate-limits.md#github-rate-limits).

## Next step

Follow the [Quick start](./quick-start.md) to create a policy and run your first scan.
