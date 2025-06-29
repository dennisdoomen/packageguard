<div align="center">
  <img src="./PackageGuard.png" style="width:300px" alt="PackageGuard"/>
</div>
<h1 align="center">Get a grip on your open-source packages</h4>

<div align="center">

[![](https://img.shields.io/github/actions/workflow/status/dennisdoomen/packageguard/build.yml?branch=main)](https://github.com/dennisdoomen/packageguard/actions?query=branch%3amain)
[![Coveralls branch](https://img.shields.io/coverallsCoverage/github/dennisdoomen/packageguard?branch=main)](https://coveralls.io/github/dennisdoomen/packageguard?branch=main)
[![](https://img.shields.io/github/release/dennisdoomen/packageguard.svg?label=latest%20release&color=007edf)](https://github.com/dennisdoomen/packageguard/releases/latest)
[![](https://img.shields.io/nuget/dt/packageguard.svg?label=downloads&color=007edf&logo=nuget)](https://www.nuget.org/packages/packageguard)
[![](https://img.shields.io/librariesio/dependents/nuget/packageguard.svg?label=dependent%20libraries)](https://libraries.io/nuget/packageguard)
![GitHub Repo stars](https://img.shields.io/github/stars/dennisdoomen/packageguard?style=flat)
[![GitHub contributors](https://img.shields.io/github/contributors/dennisdoomen/packageguard)](https://github.com/dennisdoomen/packageguard/graphs/contributors)
[![GitHub last commit](https://img.shields.io/github/last-commit/dennisdoomen/packageguard)](https://github.com/dennisdoomen/packageguard)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/dennisdoomen/packageguard)](https://github.com/dennisdoomen/packageguard/graphs/commit-activity)
[![open issues](https://img.shields.io/github/issues/dennisdoomen/packageguard)](https://github.com/dennisdoomen/packageguard/issues)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg?style=flat-square)](https://makeapullrequest.com)
![](https://img.shields.io/badge/release%20strategy-githubflow-orange.svg)
![Static Badge](https://img.shields.io/badge/8.0-dummy?label=dotnet&color=%235027d5)


<a href="#about">About</a> •
<a href="#download">Download</a> •
<a href="#how-do-i-configure-it">How To Configure</a> •
<a href="#how-do-i-use-it">How To Use</a> •
<a href="#roadmap">Roadmap</a> •
<a href="#building">Building</a> •
<a href="#contributing">Contributing</a> •
<a href="#versioning">Versioning</a> •
<a href="#credits">Credits</a> •

</div>

## About

### What's this?

PackageGuard is a fully open-source tool to scan the NuGet dependencies of your .NET solutions against a deny- or allowlist to control the open-source licenses that you want to allow or certain versions of certain packages you want to enforce or avoid. 

### What's so special about that?

I've noticed that the commercial solutions for this are usually very expensive and have functionality that smaller companies may not need. Hopefully this little tools fills the gap between tools like GitHub's Dependabot and expensive commercial products like Blackduck, SNYK  and others.

### Who created this?
My name is Dennis Doomen and I'm a Microsoft MVP and Principal Consultant at [Aviva Solutions](https://avivasolutions.nl/) with 28 years of experience under my belt. As a software architect and/or lead developer, I specialize in designing full-stack enterprise solutions based on .NET as well as providing coaching on all aspects of designing, building, deploying and maintaining software systems. I'm the author of several open-source projects such as [Fluent Assertions](https://www.fluentassertions.com), [Reflectify](https://github.com/dennisdoomen/reflectify), [Liquid Projections](https://www.liquidprojections.net), and I've been maintaining [coding guidelines for C#](https://www.csharpcodingguidelines.com) since 2001.

Contact me through [Email](mailto:dennis.doomen@avivasolutions.nl), [Bluesky](https://bsky.app/profile/dennisdoomen.com), [Twitter/X](https://twitter.com/ddoomen) or [Mastadon](https://mastodon.social/@ddoomen)

## Download

The tool is available as [a NuGet Tool package](https://www.nuget.org/packages/packageguard) and can be installed like this:

  `dotnet tool install PackageGuard --global`

Then use `packageguard --help` to see a list of options.

```
USAGE:
    PackageGuard.dll [path] [OPTIONS]

ARGUMENTS:
    [path]    The path to a directory containing a .sln file, a specific .sln file, or a specific .csproj file. Defaults to the current working directory

OPTIONS:
    -h, --help                   Prints help information
        --configPath             The path to the configuration file. Defaults to the config.json in the current working directory
        --restore-interactive    Allow enabling or disabling an interactive mode of "dotnet restore". Defaults to true
        --force-restore          Force restoring the NuGet dependencies, even if the lockfile is up-to-date
        --skip-restore           Prevent the restore operation from running, even if the lock file is missing or out-of-date
```

## How do I configure it?

First, you need to create a JSON configuration file listing the packages and/or licenses you want to allow/deny list. By default, this file is called `config.json` and loaded from the working directory, but you can override that using the `--configpath` CLI parameter. The config file needs to have the following format:

```json
{
    "settings": {
        "allow": {
          "licenses": [
              "Apache-2.0", // Uses SPDX naming
              "MIT",
          ],
          "packages": [
              "MyPackage/[7.0.0,8.0.0)"
          ]            
        },
        "deny": {
          "licenses": [],
          "packages": [
            "ProhibitedPackage"
          ]
        },
        "ignoredFeeds": [
          "https://pkgs.dev.azure.com/somecompany/project/_packaging/myfeed/nuget/v3/index.json"
        ]
    }
}
```

In this example, only NuGet packages with the MIT or Apache 2.0 licenses are allowed, the use of the package `ProhibitedPackage` is prohibited, and `MyPackage` should stick to version 7 only. Both the `allow` and `deny` sections support the `licenses` and `packages` properties. But licenses and packages listed under `allow` have precedence over those under the `deny` section.

License names are case-insensitive and follow the [SPDX identifier](https://spdx.org/licenses/) naming conventions, but we have special support for certain proprietary Microsoft licenses such as used by the `Microsoft.AspNet.WebApi*` packages. For those, we support using the license name `Microsoft .NET Library License`.

Package names can include just the NuGet ID but may also include a [NuGet-compatible version (range)](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning?tabs=semver20sort) separated by `/`. Here's a summary of the possible notations:


| Notation        | Valid versions     |
|-----------------|--------------------|
| "Package/1.0"   | 1.0            |
| "Package/[1.0,)"| v ≥ 1.0            |
| "Package/(1.0,)"          | v > 1.0            |
| "Package/[1.0]"           | v == 1.0           |
| "Package/(,1.0]"          | v ≤ 1.0            |
| "Package/(,1.0)"          | v < 1.0            |
| "Package/[1.0,2.0]"       | 1.0 ≤ v ≤ 2.0      |
| "Package/(1.0,2.0)"       | 1.0 < v < 2.0      |
| "Package/[1.0,2.0)"       | 1.0 ≤ v < 2.0      |

You can also tell PackageGuard to allow all packages from a particular feed, even if a package on that feed doesn't meet the licenses or packages listed under `allow`. Just add the element `feeds` under the `allow` element and specify a wildcard pattern that matches the name or URL of the feed.

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

## How do I use it?

With this configuration in place, simply invoke PackageGuard like this

`packageguard --configpath <path-to-config-file> <path-to-solution-file-or-project>`

If you pass a directory, PackageGuard will try to find the `.sln` files there. But you can also specify a specific `.csproj` to scan. 

If everything was configured correctly, you'll get something like:

![](Docs/Screenshot.png)

The exit code indicates either 0 for success or 1 for failure. 

## Roadmap

This is a rough list of items from my personal backlog that I'll be working on the coming weeks.

**Complete the MVP**
- Allow specifying the location of `dotnet.exe`
- Allow ignoring certain .csproj files or folders using Globs or wildcards (e.g. build.csproj)
- Allow marking all violations as a warning
- Allow marking individual violations as a warning
- Make sure passing settings through environment variables work
- Add caching of licenses 
- Allow specifying a GitHub token to prevent rate limiting
- Expose the internal engine through the `PackageGuard.Core` NuGet package
- Add direct support for [Nuke](https://nuke.build/)
- Allow loading settings from the directory of the scanned project and move up if not found
- Display the reason why a package was marked as a violation

**Major features**
- Add support for the new .slnx file
- Add NPM support

## Building

To build this repository locally, you need the following:
* The [.NET SDK](https://dotnet.microsoft.com/en-us/download/visual-studio-sdks) for .NET 8.0.
* Visual Studio, [JetBrains Rider](https://www.jetbrains.com/rider/) or [Visual Studio Code](https://code.visualstudio.com/) with the [C# DevKit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)

You can also build, run the unit tests and package the code using the following command-line:

`build.ps1`

Or, if you have, the [Nuke tool installed](https://nuke.build/docs/getting-started/installation/):

`nuke`

Also try using `--help` to see all the available options or `--plan` to see what the scripts does.

## Contributing

Your contributions are always welcome! Please have a look at the [contribution guidelines](CONTRIBUTING.md) first.

Previous contributors include:

<a href="https://github.com/dennisdoomen/packageguard/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=dennisdoomen/packageguard" alt="contrib.rocks image" />
</a>

(Made with [contrib.rocks](https://contrib.rocks))

## Versioning
This library uses [Semantic Versioning](https://semver.org/) to give meaning to the version numbers. For the versions available, see the [tags](/releases) on this repository.

## Credits
This library wouldn't have been possible without the following tools, packages and companies:

* [Spectre.Console](https://spectreconsole.net/) - a .NET library that makes it easier to create beautiful console applications.
* [Nuke](https://nuke.build/) - Smart automation for DevOps teams and CI/CD pipelines by [Matthias Koch](https://github.com/matkoch)
* [CliWrap](https://github.com/Tyrrrz/CliWrap) - Library for running command-line processes by [Oleksii Holub](https://github.com/Tyrrrz)
* [Coverlet](https://github.com/coverlet-coverage/coverlet) - Cross platform code coverage for .NET by [Toni Solarin-Sodara](https://github.com/tonerdo)
* [GitVersion](https://gitversion.net/) - From git log to SemVer in no time
* [ReportGenerator](https://reportgenerator.io/) - Converts coverage reports by [Daniel Palme](https://github.com/danielpalme)
* [StyleCopyAnalyzer](https://github.com/DotNetAnalyzers/StyleCopAnalyzers) - StyleCop rules for .NET
* [Roslynator](https://github.com/dotnet/roslynator) - A set of code analysis tools for C# by [Josef Pihrt](https://github.com/josefpihrt)
* [CSharpCodingGuidelines](https://github.com/bkoelman/CSharpGuidelinesAnalyzer) - Roslyn analyzers by [Bart Koelman](https://github.com/bkoelman) to go with the [C# Coding Guidelines](https://csharpcodingguidelines.com/)
* [Meziantou](https://github.com/meziantou/Meziantou.Framework) - Another set of awesome Roslyn analyzers by [Gérald Barré](https://github.com/meziantou)
* [Verify](https://github.com/VerifyTests/Verify) - Snapshot testing by [Simon Cropp](https://github.com/SimonCropp)

## Support the project
* [Sponsor me](https://github.com/sponsors/dennisdoomen)
* [Tip Me](https://paypal.me/fluentassertions)
* [Buy me a Coffee](https://ko-fi.com/dennisdoomen)

## You may also like

* [My Blog](https://www.dennisdoomen.com)
* [Reflectify](https://github.com/dennisdoomen/reflectify) - Reflection extensions without causing dependency pains
* [Pathy](https://github.com/dennisdoomen/pathy?tab=readme-ov-file#readme) - Fluently building and using file and directory paths without binary dependencies
* [.NET Library Starter Kit](https://github.com/dennisdoomen/dotnet-package-templates) - A battle-tested starter kit for building open-source and internal NuGet libraries using "dotnet new", born from half a billion downloads
* [FluentAssertions](https://github.com/fluentassertions/fluentassertions) - Extension methods to fluently assert the outcome of .NET tests
* [C# Coding Guidelines](https://csharpcodingguidelines.com/) - Forkable coding guidelines for all C# versions

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
