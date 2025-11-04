# Copilot Instructions for PackageGuard

## About This Repository

PackageGuard is a .NET tool that scans NuGet dependencies against allow/deny lists to control open-source licenses and package versions in .NET solutions.

## Project Structure

- **Src/PackageGuard** - Main CLI application (NuGet Tool package)
- **Src/PackageGuard.Core** - Core library with analysis engine
- **Src/PackageGuard.Specs** - Unit tests using XUnit
- **Src/PackageGuard.ApiVerificationTests** - API verification tests using Verify
- **Build/** - Nuke build configuration
- **.packageguard/** - Configuration files for self-scanning

## Technology Stack

- **.NET 8.0** - Primary target framework
- **C# 12** - Language version
- **Nuke** - Build automation system
- **XUnit** - Testing framework
- **FluentAssertions** - Assertion library for tests
- **Verify** - Snapshot testing tool
- **Spectre.Console** - Console UI library

## Build & Test

### Building the Project

Use the Nuke build system:
```bash
# Windows
./build.ps1

# Linux/macOS
./build.sh
```

Or using the Nuke global tool:
```bash
nuke
```

### Running Tests

Tests are automatically run during the build process. To run tests separately:
```bash
dotnet test Src/PackageGuard.Specs/PackageGuard.Specs.csproj
```

Tests should:
- Follow the **Arrange-Act-Assert** pattern
- Use **FluentAssertions** for assertions
- Use **Verify** for snapshot testing where appropriate
- Maintain or increase code coverage (tracked via Coveralls)

### Running PackageGuard

```bash
dotnet run --project Src/PackageGuard --no-build -- <path> [options]
```

## Coding Standards

This project follows the [C# Coding Guidelines](https://csharpcodingguidelines.com/). Key points:

- Use **explicit types** instead of `var` where it improves readability
- Follow **naming conventions**: PascalCase for public members, camelCase for parameters and local variables
- Add **XML documentation** for public APIs
- Keep methods **small and focused**
- Use **meaningful names** that explain intent
- Prefer **composition over inheritance**

### Code Analysis

The project uses multiple analyzers:
- **StyleCop Analyzers** - Style enforcement
- **Roslynator** - Code analysis
- **CSharpGuidelinesAnalyzer** - Guidelines enforcement
- **Meziantou.Analyzer** - Additional best practices
- **ReSharper InspectCode** - Static analysis (runs during build)

All code must pass these analyzers without warnings.

## API Changes

When changing public APIs:
1. Discuss and get approval in an issue with the `api-approved` label
2. After making changes, run `AcceptApiChanges.ps1` (Windows) or `AcceptApiChanges.sh` (Linux/macOS)
3. Commit the updated API verification snapshots

## Testing Guidelines

### Unit Test Structure

Tests should be named descriptively using the pattern:
```csharp
[Fact]
public void When_condition_Then_expected_result()
```

Example from the codebase:
```csharp
[Fact]
public async Task When_package_has_allowed_license_Then_should_not_report_violation()
{
    // Arrange
    var analyzer = new NuGetPackageAnalyzer(...);
    
    // Act
    var result = await analyzer.AnalyzeAsync(...);
    
    // Assert
    result.Should().NotContainViolations();
}
```

### Snapshot Testing

For testing complex output or data structures, use Verify:
```csharp
[Fact]
public async Task TestName()
{
    var result = GetComplexResult();
    await Verify(result);
}
```

## Configuration

PackageGuard uses hierarchical JSON configuration:
- Solution-level: `packageguard.config.json` or `.packageguard/config.json`
- Project-level: Same naming in project directories
- Settings merge with project-level taking precedence

## Dependencies

- Prefer **stable packages** over pre-release versions
- Keep dependencies **up-to-date** (Dependabot is configured)
- New dependencies should be approved by maintainers
- Check license compatibility before adding dependencies

## Common Tasks

### Adding a New License Type

1. Update `LicenseFetcher.cs` to recognize the license
2. Add tests in `LicenseFetcherSpecs.cs`
3. Update documentation if needed

### Adding a New Package Source

1. Modify `FetchingStrategies/` to add new strategy
2. Add corresponding tests
3. Update configuration schema if needed

### Modifying Violation Detection

1. Update logic in `NuGetPackageAnalyzer.cs` or `CSharpProjectAnalyzer.cs`
2. Add comprehensive test cases
3. Ensure existing tests still pass
4. Run `AcceptApiChanges` if public API changed

## Pull Request Guidelines

1. **Target** the `develop` branch (not `main`)
2. **Keep changes focused** and minimal
3. **Include tests** for new functionality
4. **Update documentation** for user-facing changes
5. **Maintain code coverage** (must not decrease)
6. **Pass all CI checks** including code analysis
7. **Follow commit message conventions**: Use clear, descriptive messages

## Important Notes

- This is a **global .NET tool**, so consider the user experience when running from CLI
- **Performance matters** - the tool needs to scan large solutions efficiently
- **Caching** is used to speed up repeated scans (see `--use-caching` option)
- **GitHub rate limits** can affect license fetching - support for API keys is built in
- The tool uses `dotnet restore` internally - handle this carefully in code changes

## Versioning

- Uses **GitVersion** for semantic versioning
- Follows **GitHub Flow** branching strategy
- Tags on `main` branch trigger NuGet package publishing

## Resources

- [C# Coding Guidelines](https://csharpcodingguidelines.com/)
- [Contributing Guide](../CONTRIBUTING.md)
- [Nuke Build Documentation](https://nuke.build/)
- [FluentAssertions Documentation](https://fluentassertions.com/)
- [Verify Documentation](https://github.com/VerifyTests/Verify)
