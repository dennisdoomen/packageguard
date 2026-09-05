---
sidebar_position: 2
---

# Installation

The tool is available in two forms.

## As a .NET Global Tool (recommended)

Install it as [a NuGet tool package](https://www.nuget.org/packages/packageguard):

```bash
dotnet tool install PackageGuard --global
```

Then use `packageguard --help` to see the list of options.

## As a portable deployment

Download the latest `PackageGuard-{version}.zip` from the [releases page](https://github.com/dennisdoomen/packageguard/releases/latest), extract it, and run it using `dotnet PackageGuard.dll`. This requires .NET 9.0 or later to be installed, but works on Windows, Linux, and macOS.

## Command-line options

```
USAGE:
    PackageGuard [path] [OPTIONS]            # When installed as a global tool
    dotnet PackageGuard.dll [path] [OPTIONS] # When using the portable deployment

ARGUMENTS:
    [path]    The path to a directory containing a .sln/.slnx file and/or a package.json, a specific .sln/.slnx file, a
              specific .csproj file, or a specific package.json. Defaults to the current working directory

OPTIONS:
                                 DEFAULT
    -h, --help                              Prints help information
    -c, --config-path                       The path to the configuration file. Defaults to hierarchical discovery of
                                            packageguard.config.json or .packageguard/config.json files starting from
                                            the solution directory
    -i, --restore-interactive    True       Allow enabling or disabling an interactive mode of "dotnet restore".
                                            Defaults to true
        --ignore-violations                 Don't fail the analysis if any violations are found. Defaults to false
    -f, --force-restore                     Force restoring the NuGet dependencies, even if the lockfile is up-to-date
    -s, --skip-restore                      Prevent the restore operation from running, even if the lock file is missing
                                            or out-of-date
    -a, --github-api-key                    GitHub API key to use for fetching package licenses. If not specified, you
                                            may run into GitHub's rate limiting issues
        --use-caching                       Maintains a cache of the package information to speed up future analysis
        --cache-file-path                   Overrides the file path where analysis data is cached. Defaults to the
                                            "<workingdirectory>/.packageguard/cache.bin"
        --refresh-risk-cache                Force --report-risk to rebuild risk-related package data instead of
                                            reusing cached risk entries
        --risk-cache-max-age-hours 24       Maximum age in hours for cached risk-related package data before
                                            --report-risk refreshes it
        --nuget                  True       Explicitly enable or disable scanning for .csproj, .sln or .slnx files
        --npm                               Explicitly specify the package manager to use (npm, yarn, pnpm), or None
                                            to disable NPM scanning entirely. If not specified, it will detect it
                                            automatically
        --npm-exe-path                      The path to the npm, yarn or pnpm executable. If not specified, the system
                                            PATH is used
        --report-risk                       Show a colored risk summary in the console and generate detailed HTML/SARIF
                                            risk reports. Optionally provide a directory or file path. Directories
                                            receive generated file names; explicit filenames are used directly and may
                                            overwrite prior files
```
