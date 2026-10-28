---
sidebar_label: CI integration
---

# CI integration

PackageGuard is a normal command-line tool, so it runs in any CI system without a plugin. A scan exits with code `1` when a package is denied, which fails the build step. See [exit codes](./usage.md#exit-codes).

## GitHub Actions

This workflow checks the policy, writes a risk report, and uploads the SARIF file to GitHub code scanning. The findings then show up in the **Security** tab of your repository.

```yaml
name: PackageGuard

on: [push, pull_request]

permissions:
  contents: read
  security-events: write # to upload the SARIF file

jobs:
  packageguard:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x

      - name: Install PackageGuard
        run: dotnet tool install PackageGuard --global

      - name: Run PackageGuard
        run: packageguard . --restore-interactive false --use-caching --report-risk PackageGuardReports/
        env:
          GITHUB_API_KEY: ${{ secrets.GITHUB_TOKEN }}

      - name: Upload SARIF file
        if: always()
        uses: github/codeql-action/upload-sarif@v3
        with:
          sarif_file: PackageGuardReports
          category: packageguard

      - name: Keep the HTML report
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: packageguard-report
          path: PackageGuardReports
```

Some notes:

- `--restore-interactive false` makes sure `dotnet restore` never waits for a sign-in prompt.
- `--report-risk PackageGuardReports/` writes the reports to a known folder instead of a temporary one, so the next steps can find them. You can also set the `PACKAGEGUARD_REPORT_DIRECTORY` environment variable.
- `if: always()` uploads the reports even when PackageGuard fails the build.
- Leave out `--report-risk` and the two upload steps if you only want the license check. That run is much faster.

## The GitHub API key

Both license lookups and risk scoring call the GitHub API. Without a key, you hit GitHub's rate limit of 60 requests per hour very quickly. See [Caching and rate limits](./caching-and-rate-limits.md#github-rate-limits).

- In GitHub Actions, the built-in `GITHUB_TOKEN` works, as in the example above.
- In other CI systems, create a personal access token with the `public_repo` scope, store it as a secret, and expose it as the `GITHUB_API_KEY` environment variable.

## Caching in CI

A CI agent usually starts empty, so every run collects all package data again. To avoid that, either:

- commit the `.packageguard/*.bin` cache files to your repository, as described in [Caching](./caching-and-rate-limits.md#caching), or
- use the cache feature of your CI system for the `.packageguard` folder, for example `actions/cache` in GitHub Actions.

Always pass `--use-caching`, so that PackageGuard reads and updates those files.

## Adopting a policy step by step

When you add PackageGuard to an existing codebase, or add a new rule, you may get more violations than you can fix at once. To keep the build green while you work through them:

- Set the `PACKAGEGUARD_DENY_AS_WARNING` environment variable in the pipeline, or pass `--treat-deny-as-warning`. Denied packages are then reported as warnings. Known malicious packages still fail the build.
- Put licenses or packages you want to replace in the [`warn` section](./configuration.md#warnings-instead-of-build-failures) instead of `deny`.
- Use [risk exceptions](./risk-gates.md#risk-exceptions) with an `expiresOn` date for risks you accept for now.

## Other checks

- Run [`packageguard dependencies --severity error`](./dependencies.md) to fail the build on redundant package references.
- Add [`--sbom`](./sbom.md) to publish a Software Bill of Materials with every build.
