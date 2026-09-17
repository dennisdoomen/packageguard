---
sidebar_position: 8
---

# Roadmap

This is a rough list of items from my personal backlog that I'll be working on the coming weeks, plus the
open feature requests from [the issue tracker](https://github.com/dennisdoomen/packageguard/issues). Feel
free to upvote or comment on any of the linked issues if one of them matters to you.

**Minor features**
- Allow specifying the location of `dotnet.exe`
- Allow ignoring certain .csproj files or folders using Globs or wildcards (e.g. build.csproj)
- Allow marking all violations as a warning
- Allow marking individual violations as a warning
- Expose the internal engine through the `PackageGuard.Core` NuGet package
- Add direct support for [Nuke](https://nuke.build/)
- Add a `--why` flag to `analyze` that shows an abbreviated dependency path inline on a violation, without needing a separate `explain` call
- Build a real parent-child dependency graph for npm, yarn and pnpm projects, so `--sbom` and `explain` output for those ecosystems is as accurate as it already is for NuGet

**Ecosystem support**
- [#219](https://github.com/dennisdoomen/packageguard/issues/219) Add support for Python dependencies (`requirements.txt`, `poetry.lock`, `uv.lock`)

**CI/CD and IDE integration**
- [#213](https://github.com/dennisdoomen/packageguard/issues/213) A first-party GitHub Action with SARIF violation annotations on pull requests
- [#221](https://github.com/dennisdoomen/packageguard/issues/221) MSBuild integration that reports violations as build warnings in the IDE
- [#215](https://github.com/dennisdoomen/packageguard/issues/215) A pull-request diff mode that analyses only added or upgraded packages

**Policy and configuration**
- [#222](https://github.com/dennisdoomen/packageguard/issues/222) A `packageguard init` command that scaffolds a configuration from your actual dependencies
- [#218](https://github.com/dennisdoomen/packageguard/issues/218) Built-in policy presets that configurations can extend
- [#217](https://github.com/dennisdoomen/packageguard/issues/217) Support SPDX license expressions (`OR`, `AND`, `WITH`) instead of plain identifiers
- [#225](https://github.com/dennisdoomen/packageguard/issues/225) A feed provenance policy asserting which feed a package must come from
- [#212](https://github.com/dennisdoomen/packageguard/issues/212) Allow policies to gate on risk scores and package age
- [#211](https://github.com/dennisdoomen/packageguard/issues/211) A baseline and suppression file so existing codebases can adopt PackageGuard incrementally

**Reporting and output**
- [#214](https://github.com/dennisdoomen/packageguard/issues/214) Machine-readable violation output (JSON, Markdown, SARIF, JUnit)
- [#216](https://github.com/dennisdoomen/packageguard/issues/216) Generate a third-party attribution / NOTICE file with full license texts
- [#224](https://github.com/dennisdoomen/packageguard/issues/224) Track risk scores over time and show trends in the report

**Security and supply chain**
- [#220](https://github.com/dennisdoomen/packageguard/issues/220) Detect typosquatting and dependency-confusion risks
