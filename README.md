<h1 align="center">
  <br>
  PackageGuard (Work In Progress)
  <br>
</h1>

<h4 align="center">Get a grip on your open-source packages</h4>

<div align="center">

[![](https://img.shields.io/github/actions/workflow/status/your-github-username/packageguard/build.yml?branch=main)](https://github.com/your-github-username/packageguard/actions?query=branch%3amain)
[![Coveralls branch](https://img.shields.io/coverallsCoverage/github/your-github-username/packageguard?branch=main)](https://coveralls.io/github/your-github-username/packageguard?branch=main)
[![](https://img.shields.io/github/release/your-github-username/packageguard.svg?label=latest%20release&color=007edf)](https://github.com/your-github-username/packageguard/releases/latest)
[![](https://img.shields.io/nuget/dt/packageguard.svg?label=downloads&color=007edf&logo=nuget)](https://www.nuget.org/packages/packageguard)
[![](https://img.shields.io/librariesio/dependents/nuget/packageguard.svg?label=dependent%20libraries)](https://libraries.io/nuget/packageguard)
![GitHub Repo stars](https://img.shields.io/github/stars/your-github-username/packageguard?style=flat)
[![GitHub contributors](https://img.shields.io/github/contributors/your-github-username/packageguard)](https://github.com/your-github-username/packageguard/graphs/contributors)
[![GitHub last commit](https://img.shields.io/github/last-commit/your-github-username/packageguard)](https://github.com/your-github-username/packageguard)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/your-github-username/packageguard)](https://github.com/your-github-username/packageguard/graphs/commit-activity)
[![open issues](https://img.shields.io/github/issues/your-github-username/packageguard)](https://github.com/your-github-username/packageguard/issues)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg?style=flat-square)](https://makeapullrequest.com)
![](https://img.shields.io/badge/release%20strategy-githubflow-orange.svg)
![Static Badge](https://img.shields.io/badge/4.7%2C_8.0-dummy?label=dotnet&color=%235027d5)



<a href="#about">About</a> •
<a href="#how-to-use-it">How To Use</a> •
<a href="#download">Download</a> •
<a href="#contributors">Contributors</a> •
<a href="#versioning">Versioning</a> •
<a href="#credits">Credits</a> •
<a href="#related">Related</a> •
<a href="#license">License</a>

</div>

## About

### What's this?

Add stuff like:
* PackageGuard offers
* what .NET, C# other versions of dependencies it supports

### What's so special about that?

* What makes it different from other libraries?
* Why did you create it.
* What problem does it solve?

### Who created this?
My name is Dennis Doomen and I'm a Microsoft MVP and Principal Consultant at [Aviva Solutions](https://avivasolutions.nl/) with 28 years of experience under my belt. As a software architect and/or lead developer, I specialize in designing full-stack enterprise solutions based on .NET as well as providing coaching on all aspects of designing, building, deploying and maintaining software systems. I'm the author of several open-source projects such as [Fluent Assertions](https://www.fluentassertions.com), [Reflectify](https://github.com/dennisdoomen/reflectify), [Liquid Projections](https://www.liquidprojections.net), and I've been maintaining [coding guidelines for C#](https://www.csharpcodingguidelines.com) since 2001.

Contact me through [Email](mailto:dennis.doomen@avivasolutions.nl), [Bluesky](https://bsky.app/profile/dennisdoomen.com), [Twitter/X](https://twitter.com/ddoomen) or [Mastadon](https://mastodon.social/@ddoomen)


## How do I use it?
* Code examples
* Where to find more examples

```csharp
Some example code showing your library
```

## Download

This library is available as [a NuGet package](https://www.nuget.org/packages/packageguard) on https://nuget.org. To install it, use the following command-line:

  `dotnet add package packageguard`

## Building

To build this repository locally, you need the following:
* The [.NET SDKs](https://dotnet.microsoft.com/en-us/download/visual-studio-sdks) for .NET 4.7, 6.0 and 8.0.
* Visual Studio, JetBrains Rider or Visual Studio Code with the C# DevKit

You can also build, run the unit tests and package the code using the following command-line:

`build.ps1`

Or, if you have, the [Nuke tool installed](https://nuke.build/docs/getting-started/installation/):

`nuke`

Also try using `--help` to see all the available options or `--plan` to see what the scripts does.

## Contributing

Your contributions are always welcome! Please have a look at the [contribution guidelines](CONTRIBUTING.md) first.

Previous contributors include:

<a href="https://github.com/your-github-username/packageguard/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=your-github-username/packageguard" alt="contrib.rocks image" />
</a>

(Made with [contrib.rocks](https://contrib.rocks))

## Versioning
This library uses [Semantic Versioning](https://semver.org/) to give meaning to the version numbers. For the versions available, see the [tags](/releases) on this repository.

## Credits
This library wouldn't have been possible without the following tools, packages and companies:

* [Nuke](https://nuke.build/) - Smart automation for DevOps teams and CI/CD pipelines by [Matthias Koch](https://github.com/matkoch)
* [xUnit](https://xunit.net/) - Community-focused unit testing tool for .NET by [Brad Wilson](https://github.com/bradwilson)
* [Coverlet](https://github.com/coverlet-coverage/coverlet) - Cross platform code coverage for .NET by [Toni Solarin-Sodara](https://github.com/tonerdo)
* [Polysharp](https://github.com/Sergio0694/PolySharp) - Generated, source-only polyfills for C# language features by [Sergio Pedri](https://github.com/Sergio0694)
* [GitVersion](https://gitversion.net/) - From git log to SemVer in no time
* [ReportGenerator](https://reportgenerator.io/) - Converts coverage reports by [Daniel Palme](https://github.com/danielpalme)
* [StyleCopyAnalyzer](https://github.com/DotNetAnalyzers/StyleCopAnalyzers) - StyleCop rules for .NET
* [Roslynator](https://github.com/dotnet/roslynator) - A set of code analysis tools for C# by [Josef Pihrt](https://github.com/josefpihrt)
* [CSharpCodingGuidelines](https://github.com/bkoelman/CSharpGuidelinesAnalyzer) - Roslyn analyzers by [Bart Koelman](https://github.com/bkoelman) to go with the [C# Coding Guidelines](https://csharpcodingguidelines.com/)
* [Meziantou](https://github.com/meziantou/Meziantou.Framework) - Another set of awesome Roslyn analyzers by [Gérald Barré](https://github.com/meziantou)
* [Verify](https://github.com/VerifyTests/Verify) - Snapshot testing by [Simon Cropp](https://github.com/SimonCropp)

## Support the project
* [Github Sponsors](https://github.com/sponsors/dennisdoomen)
* [Tip Me](https://paypal.me/fluentassertions)
* [Buy me a Coffee](https://ko-fi.com/dennisdoomen)
* [Sponsor Me](https://www.patreon.com/bePatron?u=9250052&redirect_uri=http%3A%2F%2Ffluentassertions.com%2F&utm_medium=widget)

## You may also like

* [My Blog](https://www.dennisdoomen.com)
* [Reflectify](https://github.com/dennisdoomen/reflectify) - Reflection extensions without causing dependency pains
* [.NET Library Package Templates](https://github.com/dennisdoomen/dotnet-package-templates) - "dotnet new" templates for building NuGet-published multi-targeting libraries with all the bells and whistles
* [FluentAssertions](https://github.com/fluentassertions/fluentassertions) - Extension methods to fluently assert the outcome of .NET tests
* [C# Coding Guidelines](https://csharpcodingguidelines.com/) - Forkable coding guidelines for all C# versions

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
