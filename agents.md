# agents.md

This file contains detailed guidance for AI agents working in the PackageGuard repository.

## Commands

### Build

```powershell
# Full build (compile, test, package)
./build.ps1

# Or with the Nuke global tool
nuke

# See all available build targets
nuke --help
nuke --plan
```

### Run tests

```bash
# All unit tests
dotnet test Src/PackageGuard.Specs/PackageGuard.Specs.csproj

# Single test method (MSTest filter syntax)
dotnet test Src/PackageGuard.Specs/PackageGuard.Specs.csproj --filter "FullyQualifiedName~Can_deny_an_entire_package"

# API verification tests
dotnet test Src/PackageGuard.ApiVerificationTests/PackageGuard.ApiVerificationTests.csproj
```

### Run the tool locally

```bash
dotnet run --project Src/PackageGuard -- <path> [options]
# Example:
dotnet run --project Src/PackageGuard -- . --config-path .packageguard/config.json
```

### Accept API snapshot changes

After changing public APIs in `PackageGuard.Core`:

```powershell
./AcceptApiChanges.ps1   # Windows
./AcceptApiChanges.sh    # Linux/macOS
```

## Architecture

PackageGuard is a .NET global CLI tool (target: .NET 9) that analyzes NuGet and NPM dependency trees against user-defined allow/deny policies and optionally scores packages on legal, security, and operational risk.

### Project layout

| Project | Role |
|---------|------|
| `Src/PackageGuard` | Spectre.Console CLI — parses arguments, wires up services, renders output |
| `Src/PackageGuard.Core` | Platform-agnostic analysis engine (NuGet package on NuGet.org) |
| `Src/PackageGuard.Specs` | MSTest unit tests; uses real `.csproj`/`package.json` fixtures under `TestCases/` |
| `Src/PackageGuard.ApiVerificationTests` | Verify snapshot tests that lock the public API surface |
| `Build/` | Nuke build project |

### Core analysis flow

1. **CLI** (`AnalyzeCommand`) builds `AnalyzerSettings` from CLI flags and resolves `GetPolicyByProject` via `ConfigurationLoader` (hierarchical JSON config discovery).
2. **`ProjectAnalyzer`** (entry point into `PackageGuard.Core`) iterates two `IProjectAnalysisStrategy` implementations in sequence:
   - `CSharpProjectAnalysisStrategy` — discovers `.sln`/`.slnx`/`.csproj` files, restores via `dotnet restore`, reads `project.assets.json` lock files with `DotNetLockFileLoader`, and calls `NuGetPackageAnalyzer` to fetch metadata and licenses.
   - `NpmProjectAnalysisStrategy` — detects the JS package manager (npm/yarn/pnpm), parses the appropriate lock file (`NpmLockFileParser`, `YarnLockFileParser`, `PnpmLockFileParser`), and fetches metadata from the npm registry via `NpmRegistryMetadataFetcher`.
3. All discovered packages accumulate in `PackageInfoCollection`, which also handles binary caching (`cache.bin` via MemoryPack).
4. **License fetching** is handled by `LicenseFetcher`, which chains strategies (`GitHubLicenseFetcher`, `UrlLicenseFetcher`, `CorrectMisbehavingPackagesFetcher`) to resolve SPDX license identifiers from NuGet/npm metadata or GitHub repository data.
5. **Policy evaluation** — each package is checked against the project's `ProjectPolicy` (merged from hierarchical config files). Violations are returned as `PolicyViolation[]`.
6. **Risk scoring** (opt-in via `--report-risk`) — `ParallelPackageRiskEnricher` fans out to multiple `IEnrichPackageRisk` enrichers (`GitHubRepositoryRiskEnricher`, `OsvRiskEnricher`, `NuGetPackageSigningRiskEnricher`, etc.) that populate `PackageInfo` with raw signals. Then `RiskEvaluator` calls `LegalRiskEvaluator`, `SecurityRiskEvaluator`, and `OperationalRiskEvaluator` (each composed of `IEvaluateRiskFactor` instances) to produce a 0–100 weighted score. The CLI writes the results to a self-contained HTML report and a SARIF file via `RiskHtmlReportWriter` / `RiskSarifReportWriter`.

### Key design patterns

- **Strategy pattern** — `IProjectAnalysisStrategy` lets NuGet and NPM scanning be extended independently.
- **Chain-of-responsibility** — `IFetchLicense` implementations try one source after another until a license is found.
- **Enricher pipeline** — `IEnrichPackageRisk` enrichers are run in parallel (via `ParallelPackageRiskEnricher`) and write directly into `PackageInfo` fields; no aggregation step is needed before scoring.
- **Risk factor decomposition** — each risk dimension (`IEvaluateRiskDimension`) composes multiple `IEvaluateRiskFactor` instances, each contributing a weighted score plus a human-readable rationale stored in `RiskFactorContribution`.

### Configuration

Config files are JSON (`packageguard.config.json` or `.packageguard/config.json`), discovered hierarchically from project directory up to solution directory. `ConfigurationLoader.GetEffectiveConfigurationForProject` merges them: arrays (licenses, packages, feeds) are unioned; booleans prefer the innermost value. The merged result is a `ProjectPolicy` with `AllowList` and `DenyList`.

## Testing conventions

- Framework: **MSTest** (`[TestClass]`, `[TestMethod]`)
- Assertions: **FluentAssertions**
- Mocking: **FakeItEasy**
- Snapshot testing: **Verify** (used in `PackageGuard.ApiVerificationTests`)
- Test fixtures: real `.csproj`/`package.json` projects live under `Src/PackageGuard.Specs/TestCases/` and are copied to the output directory; tests reference them by path.
- Test methods are named in `Can_do_something` style (underscore-separated, verb-first).

## C# coding conventions

This project follows [C# Coding Guidelines by Dennis Doomen](https://csharpcodingguidelines.com/). Key rules enforced by Roslyn analyzers (StyleCop, Roslynator, CSharpGuidelinesAnalyzer, Meziantou):

- No field name prefixes (no `_`, `m_`, `s_`)
- `var` only when the type is evident from the right-hand side; never for built-in types
- `private` fields, `internal sealed` types by default — open up visibility deliberately
- Methods ≤ 7 statements; split otherwise
- XML doc comments on all `public`, `protected`, and `internal` members
- Return read-only collection interfaces (`IEnumerable<T>`, `IReadOnlyCollection<T>`, etc.) from public members
- Strings, collections, and tasks must never be `null` from public APIs — return empty equivalents instead
- Max line length: 130 characters; indent with 4 spaces

All code must pass the analyzers without warnings before merging.

## Pull requests

- Target branch: `main` (GitHub Flow — no separate `develop` branch)
- API surface changes: run `AcceptApiChanges.ps1` and commit the updated snapshots
- Code coverage must not decrease (tracked via Coveralls)
