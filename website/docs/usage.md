---
sidebar_position: 4
---

# Usage

With a [configuration](./configuration.md) in place, simply invoke PackageGuard like this:

```bash
packageguard --configpath <path-to-config-file> <path-to-solution-file-or-project>
```

If you pass a directory, PackageGuard will try to find the `.sln`, `.slnx` or `package.json` files there. But you can also specify a specific `.csproj` or `package.json` to scan.

If everything was configured correctly, you'll get something like:

![Console output of a PackageGuard scan](/img/console-output.png)

The exit code indicates either 0 for success or 1 for failure.

## Where to next?

- [Risk Metrics](./risk-metrics.md) - score every package on legal, security and operational risk
- [Software Bill of Materials](./sbom.md) - emit the dependency graph as CycloneDX or SPDX
- [Caching and rate limits](./caching-and-rate-limits.md) - make repeated scans fast and avoid GitHub throttling
