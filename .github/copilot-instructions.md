# PackageGuard
PackageGuard is a .NET 9.0 CLI tool for scanning NuGet dependencies against allow/deny lists to control open-source licenses and package versions in .NET solutions.

Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

## Working Effectively

### Prerequisites and Environment Setup
- **CRITICAL**: Install .NET 9.0 SDK (NOT .NET 8.0 despite what README states) from https://dot.net/v1/dotnet-install.sh
  - Run `wget https://dot.net/v1/dotnet-install.sh && chmod +x dotnet-install.sh`
  - Run `./dotnet-install.sh --channel 9.0` 
  - Set `export PATH="/home/runner/.dotnet:$PATH"` and `export DOTNET_ROOT="/home/runner/.dotnet"`
- Verify installation: `dotnet --version` should show 9.0.x
- **BUILD SYSTEM NOTE**: The Nuke build system may fail in shallow git clones (common in CI) due to GitVersion issues. Use direct dotnet commands instead.

### Building and Testing (VALIDATED COMMANDS)
- **Initial setup**:
  - `ln -s Build build` (creates symlink to fix case sensitivity issue with build scripts)
  - `mkdir -p Artifacts && echo "# PackageGuard Tool" > Artifacts/Readme.md` (required for packaging)

- **Build Commands** (NEVER CANCEL - set timeout 60+ minutes):
  - `dotnet restore` -- takes ~35 seconds. NEVER CANCEL. Set timeout to 5+ minutes.
  - `dotnet build --no-restore` -- takes ~8 seconds. NEVER CANCEL. Set timeout to 5+ minutes.
  - Alternative: `dotnet build` (restore + build) -- takes ~40 seconds total. NEVER CANCEL.

- **Test Commands** (NEVER CANCEL - set timeout 30+ minutes):
  - `dotnet test --no-build` -- takes ~38 seconds. NEVER CANCEL. Set timeout to 10+ minutes.
  - **EXPECTED**: Many tests fail with "403 (Forbidden)" due to GitHub API rate limiting - this is normal behavior
  - Test projects: `PackageGuard.Specs` (unit tests), `PackageGuard.ApiVerificationTests` (API verification)

- **Build using Nuke** (may fail in CI environments):
  - `./build.sh --help` -- shows available targets
  - `./build.sh --target Compile` -- may fail with GitVersion issues in shallow clones
  - If Nuke fails, use direct dotnet commands above instead

### Running PackageGuard CLI Tool

#### Method 1: Direct dotnet run
- `dotnet run --project Src/PackageGuard/PackageGuard.csproj -- --help`
- `dotnet run --project Src/PackageGuard/PackageGuard.csproj -- . --config-path path/to/config.json --skip-restore`

#### Method 2: Install as global tool
- `dotnet pack Src/PackageGuard/PackageGuard.csproj -o ./artifacts --configuration Release`
- `dotnet tool install --global PackageGuard --add-source ./artifacts --version 1.0.0`
- `PackageGuard --help`
- `PackageGuard . --config-path config.json`

## Validation

### Manual Testing Requirements
Always test PackageGuard functionality after making changes:
1. **Basic functionality**: `PackageGuard --help` should show usage information
2. **Analysis test**: `PackageGuard . --config-path Samples/config.json --skip-restore --ignore-violations`
   - Should process projects and show analysis progress
   - May fail with GitHub rate limiting (403 errors) - this is expected behavior
3. **Configuration test**: Create test config and run analysis to verify policy enforcement works

### Common Analysis Issues
- **GitHub Rate Limiting**: "Response status code does not indicate success: 403 (Forbidden)" 
  - This is expected behavior when analyzing packages without GitHub API key
  - Set `GITHUB_API_KEY` environment variable or use `--github-api-key` parameter to avoid
- **Missing lock files**: Some projects may show "Failed to load the lock file" - this is normal for build projects

## Key Project Structure

### Repository Root
```
.
├── README.md                    # Project documentation 
├── CONTRIBUTING.md              # Contribution guidelines
├── PackageGuard.sln            # Main solution file
├── global.json                 # .NET SDK version (specifies 8.0 but projects use 9.0)
├── build.sh/.ps1/.cmd          # Nuke build scripts
├── Build/                      # Nuke build project (uppercase B)
│   └── _build.csproj           # Build automation project
├── Src/                        # Source code
│   ├── PackageGuard/           # Main CLI tool project
│   ├── PackageGuard.Core/      # Core analysis library
│   ├── PackageGuard.Specs/     # Unit tests (xUnit)
│   └── PackageGuard.ApiVerificationTests/  # API verification tests
├── Samples/                    # Sample configuration files
│   └── config.json            # Example policy configuration
└── .github/workflows/build.yml # CI/CD pipeline
```

### Important Files for Development
- `Src/PackageGuard/AnalyzeCommand.cs` - Main analysis command implementation
- `Src/PackageGuard.Core/NuGetPackageAnalyzer.cs` - Core package analysis logic
- `Src/PackageGuard.Core/LicenseFetcher.cs` - License information retrieval
- `AcceptApiChanges.sh`/`AcceptApiChanges.ps1` - Scripts to update API verification baselines

## Common Development Tasks

### Making Code Changes
1. Always run build and test validation after changes: `dotnet build && dotnet test`
2. For public API changes: Run `./AcceptApiChanges.sh` (Linux) or `AcceptApiChanges.ps1` (Windows)
3. Always test PackageGuard CLI functionality manually using validation steps above

### Troubleshooting Build Issues
- **"Project file does not exist: build/_build.csproj"**: Run `ln -s Build build` to fix case sensitivity
- **GitVersion errors**: Use direct dotnet commands instead of Nuke build system
- **Missing Artifacts directory**: Run `mkdir -p Artifacts && echo "# PackageGuard Tool" > Artifacts/Readme.md`
- **Test failures with 403 errors**: This is expected - GitHub API rate limiting affects license fetching tests

### Configuration Files
PackageGuard uses hierarchical configuration discovery:
1. `packageguard.config.json` in solution directory
2. `.packageguard/config.json` in solution directory  
3. `packageguard.config.json` in project directories
4. `.packageguard/config.json` in project directories

Example config structure:
```json
{
    "settings": {
        "allow": {
            "licenses": ["MIT", "Apache-2.0"],
            "packages": ["MyPackage/[7.0.0,8.0.0)"]
        },
        "deny": {
            "packages": ["ProhibitedPackage"]
        }
    }
}
```

## Version Discrepancy Warning
**CRITICAL**: The README.md states ".NET SDK for .NET 8.0" but the projects actually target .NET 9.0. Always use .NET 9.0 SDK for building and running this project. The README information is outdated.