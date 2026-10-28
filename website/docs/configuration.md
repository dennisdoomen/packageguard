---
sidebar_label: Configuration file
---

# Configuration file

A configuration file holds your policy: the licenses, packages, versions and feeds you allow, deny or want a warning about. You don't need to write one by hand to get started. Run [`packageguard init`](./init.md) to generate one from the licenses your dependencies actually use, and edit it from there.

Risk-based rules, such as a maximum risk score or a minimum package age, live in the same file. They are described in [Risk gates and exceptions](./risk-gates.md).

## Configuration format

A configuration file looks like this:

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

This example does the following:

- It allows NuGet and npm packages with the MIT or Apache-2.0 license.
- It always allows packages whose ID starts with `Microsoft.Extensions.`, and packages that come from a feed on `dev.azure.com`.
- It only allows `MyPackage` in version 7.
- It denies `ProhibitedPackage`, every package whose ID starts with `Legacy.`, and all pre-release versions (such as `1.0.2-beta.2`).

Both the `allow` and `deny` sections support the `licenses` and `packages` properties.

:::warning
Deny rules always take precedence over allow rules. A package that matches the `deny` section is blocked, whatever the `allow` section says.
:::

Configuration files may contain comments. Both `// line` and `/* block */` comments are ignored, so you can explain why a license or package is allowed or denied.

## Where PackageGuard looks for configuration

PackageGuard finds configuration files automatically, based on your solution and project structure. You can define a repository-wide policy at the solution level and add project-specific rules where you need them. It looks in this order:

1. **Solution level**: `packageguard.config.json` in the same folder as your `.sln`, `.slnx` or `package.json` file
2. **Solution level**: `config.json` in a `.packageguard` subfolder of your solution or `package.json` folder
3. **Project level**: `packageguard.config.json` in individual project folders
4. **Project level**: `config.json` in a `.packageguard` subfolder of project folders

PackageGuard merges the settings of all files it finds. For boolean values, project-level settings take precedence over solution-level settings. Arrays (packages, licenses, feeds) are combined. PackageGuard scans one `package.json` per run, so for npm it uses the configuration that belongs to that folder.

### Example: multi-level configuration

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

The effective configuration for `WebProject` is:

- Allowed licenses: MIT, Apache-2.0, BSD-3-Clause (merged)
- Allowed packages: `Microsoft.*`, `System.*`, `WebSpecificPackage/[1.0.0,2.0.0)` (merged)
- Denied packages: UnsafePackage (inherited)

### Using a specific file

Use `--config-path` to skip the automatic discovery and use one file:

```bash
packageguard --config-path path/to/my-config.json
```

If the file does not exist, PackageGuard stops with an error instead of using the default policy. That way a typo in the path doesn't go unnoticed.

### Running without a configuration

If PackageGuard finds no configuration file, it still runs. It then allows only well-known permissive licenses, such as MIT, Apache-2.0, the BSD licenses and ISC, and applies no other rules. A package with any other license, or with a license that could not be found, is reported as a violation. PackageGuard prints a warning about this once per run.

## Licenses

License names are case-insensitive and follow the [SPDX identifier](https://spdx.org/licenses/) naming conventions. PackageGuard also supports some proprietary Microsoft licenses, such as the one used by the `Microsoft.AspNet.WebApi*` packages. For those, use the license name `Microsoft .NET Library License`.

Many packages declare an [SPDX license expression](https://spdx.github.io/spdx-spec/v2.3/SPDX-license-expressions/) instead of a single license. PackageGuard evaluates these expressions:

| Expression | Allowed when | Denied (or warned about) when |
|---|---|---|
| `MIT OR Apache-2.0` | any option is allowed, because you can choose | every option is denied |
| `MIT AND Unicode-DFS-2016` | every license is allowed | any license is denied |
| `GPL-2.0-only WITH Classpath-exception-2.0` | the full term or `GPL-2.0-only` is allowed | only the full term is denied, because the exception relaxes the license terms |
| `Apache-2.0+` or `GPL-2.0-or-later` | the license or its base version (`Apache-2.0`, `GPL-2.0-only`) is allowed | the license itself is denied |

Some more details:

- Parentheses work as expected, and the operators are not case-sensitive.
- An entry in your configuration can also be an expression, such as `"GPL-2.0-only WITH Classpath-exception-2.0"`.
- The order of the licenses in an `OR` or `AND` does not matter. The entry `"MIT OR Apache-2.0"` also matches `Apache-2.0 OR MIT`.
- Deprecated identifiers are treated as their current form. `GPL-3.0` matches `GPL-3.0-only` and `GPL-2.0+` matches `GPL-2.0-or-later`.
- When an expression is allowed because of one of its licenses, [`explain`](./explain.md) names that license, for example `matches allow list license entry "MIT" (from "MIT OR Apache-2.0")`. When it takes more than one entry, such as both licenses of an `AND`, `explain` names all of them.

The [legal risk score](./risk-signals.md#legal-risk) follows the same rules: it uses the least restrictive option of an `OR` and the most restrictive license of an `AND`.

## Packages and versions

Package names under `allow.packages` and `deny.packages` support wildcard patterns (`*` and `?`). They can also include a [NuGet-compatible version (range)](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning?tabs=semver20sort), separated by `/`.

Examples:

- `Microsoft.Extensions.*` matches all package IDs starting with `Microsoft.Extensions.`
- `MyCompany.A*` matches IDs such as `MyCompany.Abstractions`
- `Microsoft.*/[9.0.0,10.0.0)` matches all Microsoft package IDs from version 9.0.0 (inclusive) up to 10.0.0 (exclusive)

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

## Warnings instead of build failures

A `warn` section works like `deny` for `packages`, `licenses` and `prerelease`. A match only logs a warning instead of failing the build. A `deny` match always takes precedence over a `warn` match on the same package.

```json
{
    "settings": {
        "warn": {
            "licenses": ["GPL-3.0"],
            "packages": ["some-package"]
        }
    }
}
```

To turn every `deny` violation into a warning at run time, pass `--treat-deny-as-warning` or set the `PACKAGEGUARD_DENY_AS_WARNING` environment variable. This is useful while you work through the violations of a new rule but still want to ship. Setting the variable to any value enables it. Set it to `false` (case-insensitive) to disable it explicitly. This applies to both the ordinary `deny` rules and the [risk gates](./risk-gates.md).

## Feeds

PackageGuard finds the NuGet and npm feeds the same way `dotnet`, your npm package manager or your IDE does. It also uses the configured credential providers, so authenticated and private feeds work.

To allow every package from a particular feed, even if it doesn't match the licenses or packages under `allow`, add a `feeds` element under `allow`. Each entry is a wildcard pattern that matches the name or URL of the feed.

```json
{
    "settings": {
        "allow": {
            "feeds": ["*dev.azure.com*"]
        }
    }
}
```

To stop PackageGuard from accessing a feed at all, add it to the `ignoredFeeds` element. PackageGuard may still run `dotnet restore` if `project.assets.json` doesn't exist yet, and that restore uses all available NuGet feeds. Use `--skip-restore` to prevent that.
