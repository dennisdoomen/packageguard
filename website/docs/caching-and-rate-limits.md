---
sidebar_label: Caching and rate limits
---

# Caching and rate limits

Finding licenses and collecting risk data means many calls to GitHub, OSV and the package registries. Two things keep that fast and reliable: a GitHub API key and the cache.

## GitHub rate limits

PackageGuard reads license and risk information from `api.github.com`, which applies [rate limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api?apiVersion=2022-11-28) per caller. Unauthenticated callers get 60 requests per hour. That is not enough for anything but the smallest project, and certainly not for `--report-risk`.

Create a GitHub [personal access token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens) with the `public_repo` scope to raise the limit to 5000 requests per hour. Then either:

- pass it with `--github-api-key` (or `-a`), or
- set the `GITHUB_API_KEY` environment variable.

If the limit runs out anyway, PackageGuard shows a warning like

`The GitHub API rate limit is exhausted and resets in 42 minutes.`

and finishes the analysis without the GitHub-based signals, instead of failing. Run it again after the reset to fill in what was left out. With caching enabled, that second run doesn't repeat the work that was already done.

## Caching

Pass `--use-caching` to keep the data PackageGuard collects between runs:

```bash
packageguard . --use-caching
```

PackageGuard then stores package and license information in `.packageguard/cache.bin`. If a later run finds packages that were not in the cache, it adds them after the analysis. Use `--cache-file-path` to store the cache somewhere else.

With `--report-risk`, the cache also keeps the risk data that comes from external services and package inspection. PackageGuard writes two more files next to `cache.bin`:

| File | Holds |
|------|-------|
| `.packageguard/github-responses.bin` | The GitHub responses received, with their entity tags (ETags). Later runs check them again with conditional requests, which GitHub does not count against the rate limit of an authenticated caller. |
| `.packageguard/github-repositories.bin` | The risk profile of each repository. Dozens of packages can share one repository, so its profile is collected once. |

You can commit all three files to source control, so that CI runs and your colleagues reuse what was already collected.

[`explain`](./explain.md) reads the cache if it exists, but never updates it. Run a scan with `--use-caching` first to make `explain` fast.

## How long risk data stays fresh

Risk data in the cache is reused for up to **24 hours**. After that, a `--report-risk` run collects it again for that package before it builds the report.

To change that time, use `--risk-cache-max-age-hours`:

```bash
packageguard . --report-risk --use-caching --risk-cache-max-age-hours 6
```

To collect all risk data again now, but keep using the cache for later runs, use `--refresh-risk-cache`:

```bash
packageguard . --report-risk --use-caching --refresh-risk-cache
```
