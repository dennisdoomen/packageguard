---
sidebar_label: CLI reference
---

# CLI reference

PackageGuard has four commands. Running `packageguard [path]` without a command is the same as `packageguard analyze [path]`.

| Command | What it does | Guide |
| --- | --- | --- |
| `analyze [path]` | Checks your dependencies against your policy. Optionally scores risk and writes an SBOM. | [Running a scan](./usage.md) |
| `init [path]` | Writes a configuration file based on the licenses your dependencies use. | [Creating a policy with `init`](./init.md) |
| `explain <package> [version]` | Shows why a package is present and how the policy evaluated it. | [Explaining a package](./explain.md) |
| `dependencies` | Finds redundant package references and version conflicts. | [Redundant references and version conflicts](./dependencies.md) |

When you use the [portable version](./installation.md#portable-version), replace `packageguard` with `dotnet PackageGuard.dll`.

:::note
`analyze` and `init` take the path as an argument. `explain` and `dependencies` take it with the `--path` (`-p`) option instead.
:::

Every command shows its own help with `--help`, for example `packageguard explain --help`.

## Common options

These options work the same way in every command that supports them.

| Option | Default | Description |
| --- | --- | --- |
| `[path]` or `-p`, `--path` | current folder | A folder with a `.sln`/`.slnx` file and/or a `package.json`, a specific `.sln`/`.slnx` file, a specific `.csproj` file, or a specific `package.json`. `dependencies` only supports NuGet. |
| `-c`, `--config-path` | discovered | The configuration file to use. By default, PackageGuard [discovers the configuration files](./configuration.md#where-packageguard-looks-for-configuration) itself. For `init`, the file to write. |
| `-i`, `--restore-interactive` | `true` | Allow `dotnet restore` to ask for credentials interactively. Turn this off in CI. |
| `-f`, `--force-restore` | | Always run `dotnet restore`, even if the lock file is up to date. |
| `-s`, `--skip-restore` | | Never run `dotnet restore`, even if the lock file is missing or out of date. |
| `-a`, `--github-api-key` | `GITHUB_API_KEY` | A GitHub API key, to avoid [GitHub's rate limits](./caching-and-rate-limits.md#github-rate-limits). |
| `--nuget` | `true` | Scan `.csproj`, `.sln` and `.slnx` files. |
| `--npm` | detected | The npm package manager to use: `npm`, `yarn` or `pnpm`. Use `None` to skip npm scanning. |
| `--npm-path` | | The folder, `package.json` or lock file of the npm project, if it is not in the same folder as the solution or project. |
| `--npm-exe-path` | `PATH` | The path to the `npm`, `yarn` or `pnpm` executable. |
| `-v`, `--verbose` | | Show debug output. With `--report-risk`, this includes the HTTP calls to GitHub, OSV and the npm registries. |

## analyze

```bash
packageguard analyze [path] [options]
```

Supports all common options, plus:

| Option | Default | Description |
| --- | --- | --- |
| `--ignore-violations` | | Always exit with code `0`, even if packages are denied. |
| `--treat-deny-as-warning` | `PACKAGEGUARD_DENY_AS_WARNING` | Report denied packages as warnings instead of failing. Enabled when the environment variable is set to anything other than `false`. |
| `--use-caching` | | Keep package, license and risk data in a cache between runs. See [Caching](./caching-and-rate-limits.md#caching). |
| `--cache-file-path` | `.packageguard/cache.bin` | Where to store the cache. |
| `--report-risk [PATH]` | | Show a risk summary and write HTML and SARIF reports. Optionally give a folder or file name. See [Risk reports](./risk-metrics.md). |
| `--risk-cache-max-age-hours` | `24` | How long cached risk data stays fresh. |
| `--refresh-risk-cache` | | Collect all risk data again, ignoring the cached risk data. |
| `--sbom` | | Write a Software Bill of Materials in `cyclonedx` or `spdx` format. Requires `--sbom-output`. See [SBOM](./sbom.md). |
| `--sbom-output` | | The file to write the SBOM to. |

## init

```bash
packageguard init [path] [options]
```

Supports all common options, plus:

| Option | Default | Description |
| --- | --- | --- |
| `-c`, `--config-path` | `.packageguard/config.json` | Where to write the configuration file. The default is relative to the solution or project folder. |
| `--preset` | asked | Skip the question about the kind of software: `permissive-only`, `no-network-copyleft` or `oss-friendly`. |
| `--risk-gates` | | Also add [risk-based deny rules](./risk-gates.md). |
| `--overwrite` | | Replace an existing configuration file. Without it, `init` refuses to run. |

## explain

```bash
packageguard explain <package> [version] [options]
```

| Argument or option | Default | Description |
| --- | --- | --- |
| `<package>` | | The package name. A partial or misspelled name is matched against the packages found. |
| `[version]` | | The version to explain, when the package resolved to more than one version. |
| `--cache-file-path` | `.packageguard/cache.bin` | The cache to read from, if it exists. `explain` never updates the cache. |

Also supports all common options.

## dependencies

```bash
packageguard dependencies [options]
```

| Option | Default | Description |
| --- | --- | --- |
| `--conflicts` | | Also report packages that resolve to different versions across projects. |
| `--severity` | `warning` | Use `error` to exit with code `1` when there is a finding. With `warning`, it always exits with `0`. |
| `--exclude <package>` | | Leave a package out of the results. Can be repeated. |

Also supports `--path`, the restore options and `--verbose`. It does not read the configuration or call any online service.

## Environment variables

| Variable | Effect |
| --- | --- |
| `GITHUB_API_KEY` | The default for `--github-api-key`. |
| `PACKAGEGUARD_DENY_AS_WARNING` | Enables `--treat-deny-as-warning` when set to anything other than `false`. |
| `PACKAGEGUARD_REPORT_DIRECTORY` | The folder for the risk reports, when `--report-risk` has no path. |
