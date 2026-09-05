---
sidebar_position: 7
---

# Caching and Rate Limits

## Speeding up the analysis using caching

One of the most expensive operations that PackageGuard needs to do is to find the license information from GitHub or other sources. You can significantly speed-up the analysis process by using the `--use-caching` flag. 

By default, this will cause PackageGuard to persist the license information it retrieved to a binary file under `.packageguard\cache.bin`. You can commit this file to source control so successive runs can reuse the license information it collected during a previous run. 

If PackageGuard finds new packages in your project or solution that did not exist during the previous run, then it will update the cache after the analysis is completed.  

## About the package cache

When `--use-caching` is enabled, PackageGuard stores package metadata in `.packageguard/cache.bin`. For `--report-risk`, that cache now also keeps the expensive risk-related package data that comes from external services and package inspection.

By default, cached risk-related package data is reused for up to **24 hours**. After that, a `--report-risk` run will refresh the package entry from upstream sources before rebuilding the report.

If you want to force a fully fresh risk report while still using the cache file for subsequent runs, use:

```bash
packageguard . --report-risk --use-caching --refresh-risk-cache
```

You can also tune the time-to-live for cached risk-related package data:

```bash
packageguard . --report-risk --use-caching --risk-cache-max-age-hours 6
```

## GitHub rate limiting issues

PackageGuard reads license and risk information from `api.github.com`, which applies [rate limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api?apiVersion=2022-11-28) per caller. Unauthenticated callers get 60 requests per hour, which is not enough for anything but the smallest project. Create a GitHub Personal Access Token with the `public_repo` scope to raise that to 5000 requests per hour. You can find more information about those tokens [here](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/scopes-for-oauth-apps).

After having generated such a token, pass it to PackageGuard through its `github-api-key` option or set-up an environment variable named `GITHUB_API_KEY`.

If the limit does run out, PackageGuard reports a warning like

  `The GitHub API rate limit is exhausted and resets in 42 minutes.`

and finishes the analysis without the GitHub-based signals, instead of failing. Run it again after the reset to fill in what was left out.

Combine this with `--use-caching` so that the work already done is not repeated. Alongside the package cache, PackageGuard writes two more files:

| File | Holds |
|------|-------|
| `.packageguard\github-responses.bin` | the GitHub responses received, with their entity tags. Later runs revalidate them with conditional requests, which GitHub does not charge against the rate limit of an authenticated caller. |
| `.packageguard\github-repositories.bin` | the risk profile of each repository, keyed by repository rather than by package. Dozens of packages can share one repository, and its profile is collected once. |

Both are refreshed on the schedule set by `--risk-cache-max-age-hours` (24 hours by default), and `--refresh-risk-cache` collects everything again regardless. Commit them alongside `cache.bin` if you want CI runs to benefit as well.
