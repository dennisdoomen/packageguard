#nullable enable
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.ProjectModel;
using PackageGuard.Core.CSharp;

namespace PackageGuard.Specs.CSharp;

[TestClass]
public class NuGetDependencyPathFinderSpecs
{
    private static string ProjectPath =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "..", "..", "..",
            "PackageGuard.Specs.csproj"));

    private static LockFile LoadLockFile()
    {
        var loader = new DotNetLockFileLoader { Logger = NullLogger.Instance, SkipRestore = false };
        LockFile? lockFile = loader.GetPackageLockFile(ProjectPath);
        lockFile.Should().NotBeNull("the Specs project should already be restored by the build");
        return lockFile;
    }

    [TestMethod]
    public void Finds_the_direct_chain_for_a_directly_referenced_package()
    {
        // Arrange
        LockFile lockFile = LoadLockFile();
        string version = ResolvedVersionOf(lockFile, "FluentAssertions");

        // Act
        var chains = NuGetDependencyPathFinder.FindChains(lockFile, "FluentAssertions", version);

        // Assert
        chains.Should().ContainSingle();
        chains[0].Hops.Should().ContainSingle(hop => hop.Name == "FluentAssertions");
    }

    [TestMethod]
    public void Finds_a_multi_hop_chain_for_a_transitive_package()
    {
        // Arrange
        LockFile lockFile = LoadLockFile();
        string version = ResolvedVersionOf(lockFile, "Microsoft.Testing.Platform");

        // Act
        var chains = NuGetDependencyPathFinder.FindChains(lockFile, "Microsoft.Testing.Platform", version);

        // Assert
        chains.Should().NotBeEmpty();
        chains[0].Hops.Should().HaveCountGreaterThan(1);
        chains[0].Hops[^1].Name.Should().Be("Microsoft.Testing.Platform");
        chains[0].Hops[^1].RequestedRange.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void Returns_no_chains_for_a_package_that_is_not_in_the_graph()
    {
        // Arrange
        LockFile lockFile = LoadLockFile();

        // Act
        var chains = NuGetDependencyPathFinder.FindChains(lockFile, "SomePackageThatDoesNotExist", "1.0.0");

        // Assert
        chains.Should().BeEmpty();
    }

    private static string ResolvedVersionOf(LockFile lockFile, string packageName)
    {
        return lockFile.Targets.First().Libraries
            .First(library => library.Name == packageName)
            .Version!.ToNormalizedString();
    }
}
