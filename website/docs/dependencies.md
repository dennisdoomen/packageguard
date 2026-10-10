---
sidebar_position: 4.6
---

# Finding redundant references and version conflicts

The `dependencies` command checks the dependency graph that NuGet resolved during restore, and reports two kinds of
problems that are about hygiene rather than policy:

- **Redundant references** - a `PackageReference` that is not needed, because a referenced project or another
  direct package already brings in the same package.
- **Version conflicts** - one package that resolves to different versions in different projects. This is a common
  cause of "works in one project, fails in another".

```
packageguard dependencies --path <path-to-solution-file-or-project> --conflicts
```

The command works offline. It only reads the restore output of your projects, so it does not fetch package metadata or
licenses, does not use the cache, and does not read your policy configuration. It only runs `dotnet restore` when a
project's `project.assets.json` is missing or out of date. NuGet projects only.

## Options

| Option | Description |
| --- | --- |
| `-p`, `--path` | A directory with a `.sln`/`.slnx` file, a specific `.sln`/`.slnx` file, or a `.csproj` file. Defaults to the current directory. |
| `--conflicts` | Also report version conflicts. They are left out unless you pass this option. |
| `--severity error` | Exit with code 1 when a reported finding exists. The default, `warning`, always exits with code 0. |
| `--exclude <package>` | Leave a package id out of the results. Can be repeated. |
| `-f`, `--force-restore` / `-s`, `--skip-restore` / `-i`, `--restore-interactive` | The same restore options as `analyze`. |

Conflicts only count towards `--severity error` when you also pass `--conflicts`, so the exit code never depends on a
finding that the output hides.

## How to read the results

For each redundant reference, the output names the packages or projects that already provide it, and whether removing
it is safe. A reference is only *safe to remove* when the resolved version does not change. When your project asks for
a higher version than anything else does, the finding names the version it would drop to.

A package marked `PrivateAssets="all"`, and packages that are referenced automatically, never count as a provider.
They are not a dependency of what your project ships, so removing another reference because of them would drop a
shipped dependency.

Version conflicts are grouped per target framework, so a solution that resolves differently per framework on purpose is
not reported.

## Limits

- **The graph is not your source code.** A package can be redundant in the restore graph and still be used directly in
  your code. Removing it then breaks the build. This is why findings are reported and never fixed automatically.
- **Projects must be restored.** See `--force-restore` and `--skip-restore`.
- **Project references are matched by file name.** A project whose `AssemblyName` differs from its file name is not
  matched, so a redundancy through it is missed. You get a missing finding, never wrong advice.
