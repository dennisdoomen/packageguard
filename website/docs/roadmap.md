---
sidebar_position: 8
---

# Roadmap

This is a rough list of items from my personal backlog that I'll be working on the coming weeks.

**Minor features**
- Allow specifying the location of `dotnet.exe`
- Allow ignoring certain .csproj files or folders using Globs or wildcards (e.g. build.csproj)
- Allow marking all violations as a warning
- Allow marking individual violations as a warning
- Expose the internal engine through the `PackageGuard.Core` NuGet package
- Add direct support for [Nuke](https://nuke.build/)
- Display the reason why a package was marked as a violation
- Build a real parent-child dependency graph for npm, yarn and pnpm projects, so `--sbom` output for those ecosystems is as accurate as it already is for NuGet
