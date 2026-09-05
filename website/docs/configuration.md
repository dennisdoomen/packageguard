---
sidebar_position: 3
---

# Configuration

PackageGuard supports hierarchical configuration files that are automatically discovered based on your .NET solution and project structure. This allows you to define repository-wide policies at the solution level and add project-specific rules as needed. Since PackageGuard will scan a single `package.json` per run, it will use the configuration that is associated with that directory.

## Hierarchical configuration discovery

PackageGuard will automatically look for configuration files in the following order:

1. **Solution level**: `packageguard.config.json` in the same folder as your `.sln`, `.slnx` or `package.json` file
2. **Solution level**: `config.json` in a `.packageguard` subdirectory of your solution or `package.json` folder
3. **Project level**: `packageguard.config.json` in individual project directories
4. **Project level**: `config.json` in a `.packageguard` subdirectory of project directories

Settings from multiple configuration files are merged together, with project-level settings taking precedence over solution-level settings for boolean values, while arrays (packages, licenses, feeds) are combined.

## Manual configuration path

You can still specify a custom configuration file path using the `--config-path` CLI parameter to override the hierarchical discovery:

```bash
packageguard --config-path path/to/my-config.json
```

## Configuration format

Each configuration file should follow this JSON format:

```json
{
    "settings": {
        "allow": {
          "prerelease": false,
          "licenses": [
              "Apache-2.0", // Uses SPDX naming
              "MIT",
          ],
          "packages": [
              "MyPackage/[7.0.0,8.0.0)",
              "Microsoft.Extensions.*"
          ],
          "feeds": [
            "*dev.azure.com*"
          ]
        },
        "deny": {
          "licenses": [],
          "packages": [
            "ProhibitedPackage",
            "Legacy.*"
          ]
        },
        "ignoredFeeds": [
          "https://pkgs.dev.azure.com/somecompany/project/_packaging/myfeed/nuget/v3/index.json"
        ]
    }
}
```

In this example, only NuGet and NPM packages with the MIT or Apache 2.0 licenses are allowed, the use of the package `ProhibitedPackage` and any pre-release packages (e.g. `0.1.2` or `1.0.2-beta.2`) are prohibited, and `MyPackage` should stick to version 7 only. Both the `allow` and `deny` sections support the `licenses` and `packages` properties. But licenses and packages listed under `allow` have precedence over those under the `deny` section.

:::warning
Deny rules always take precedence over allow rules. If a package is denied by the `deny` section, it will be blocked regardless of what the `allow` section specifies.
:::

## Example: multi-level configuration

**Solution-level configuration** (`MySolution/packageguard.config.json`):

```json
{
    "settings": {
        "allow": {
            "licenses": ["MIT", "Apache-2.0"],
            "packages": ["Microsoft.*", "System.*"]
        },
        "deny": {
            "packages": ["UnsafePackage"]
        }
    }
}
```

**Project-level configuration** (`MySolution/WebProject/packageguard.config.json`):

```json
{
    "settings": {
        "allow": {
            "licenses": ["BSD-3-Clause"],
            "packages": ["WebSpecificPackage/[1.0.0,2.0.0)"]
        }
    }
}
```

The effective configuration for `WebProject` will allow:

- Licenses: MIT, Apache-2.0, BSD-3-Clause (merged)
- Packages: `Microsoft.*`, `System.*`, `WebSpecificPackage/[1.0.0,2.0.0)` (merged)
- Denied packages: UnsafePackage (inherited)

## Identifying packages and licenses

License names are case-insensitive and follow the [SPDX identifier](https://spdx.org/licenses/) naming conventions, but we have special support for certain proprietary Microsoft licenses such as used by the `Microsoft.AspNet.WebApi*` packages. For those, we support using the license name `Microsoft .NET Library License`.

Package names under `allow.packages` and `deny.packages` support wildcard patterns (`*` and `?`) and can optionally include a [NuGet-compatible version (range)](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning?tabs=semver20sort) separated by `/`.

Examples:

- `Microsoft.Extensions.*` matches all package IDs starting with `Microsoft.Extensions.`
- `MyCompany.A*` matches IDs such as `MyCompany.Abstractions`
- `Microsoft.*/[9.0.0,10.0.0)` matches all Microsoft package IDs between version 9.0.0 (inclusive) and 10.0.0 (exclusive)

Here's a summary of the version-range notations:

| Notation             | Valid versions |
|----------------------|----------------|
| `Package/1.0`        | 1.0            |
| `Package/[1.0,)`     | v ≥ 1.0        |
| `Package/(1.0,)`     | v > 1.0        |
| `Package/[1.0]`      | v == 1.0       |
| `Package/(,1.0]`     | v ≤ 1.0        |
| `Package/(,1.0)`     | v < 1.0        |
| `Package/[1.0,2.0]`  | 1.0 ≤ v ≤ 2.0  |
| `Package/(1.0,2.0)`  | 1.0 &lt; v &lt; 2.0  |
| `Package/[1.0,2.0)`  | 1.0 ≤ v &lt; 2.0  |

## About feeds

PackageGuard follows the same logic for getting the applicable NuGet or NPM feeds as `dotnet`, NPM package managers or your IDE does. That also means that it will use the configured credential providers to access authenticated and private feeds.

You can tell PackageGuard to allow all packages from a particular feed, even if a package on that feed doesn't meet the licenses or packages listed under `allow`. Just add the element `feeds` under the `allow` element and specify a wildcard pattern that matches the name or URL of the feed.

```json
{
    "settings": {
        "allow": {
            "feeds": ["*dev.azure.com*"]
        }
    }
}
```

And in case you want to prevent PackageGuard from trying to access a particular feed altogether, add them to the `ignoredFeeds` element. Notice that PackageGuard may still trigger a `dotnet restore` call if the package lock file (`project.assets.json`) doesn't exist yet, unless you use the `SkipRestore` option, that will use all available NuGet feeds.
