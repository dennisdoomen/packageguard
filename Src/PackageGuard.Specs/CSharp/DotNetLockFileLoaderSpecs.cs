using System.Linq;
using FluentAssertions;
using Meziantou.Extensions.Logging.InMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.CSharp;
using Pathy;

namespace PackageGuard.Specs.CSharp;

[TestClass]
public class DotNetLockFileLoaderSpecs
{
    [TestMethod]
    public void Runs_a_dotnet_restore_if_the_lock_file_is_missing()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();

        var testProject = ChainablePath.Current / "TestCases" / "SimpleApp";
        (testProject / "obj").DeleteFileOrDirectory();
        (testProject / "bin").DeleteFileOrDirectory();

        var loader = new DotNetLockFileLoader
        {
            Logger = loggingProvider.CreateLogger("")
        };

        // Act
        loader.GetPackageLockFile((testProject / "SimpleApp.csproj").ToString());

        // Assert
        loggingProvider.Logs.Select(x => x.Message).Should().ContainMatch("*dotnet restore*--interactive*");
    }

    [TestMethod]
    public void Can_run_a_forced_dotnet_restore_even_if_the_lock_file_is_already_there()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();

        var testProject = ChainablePath.Current / "TestCases" / "SimpleApp";

        var loader = new DotNetLockFileLoader
        {
            Logger = loggingProvider.CreateLogger(""),
            ForceRestore = true
        };

        loader.GetPackageLockFile((testProject / "SimpleApp.csproj").ToString());

        // Act
        loader.GetPackageLockFile((testProject / "SimpleApp.csproj").ToString());

        // Assert
        loggingProvider.Logs.Select(x => x.Message).Should().ContainMatch("*dotnet restore*--interactive*");
    }

    [TestMethod]
    public void Can_load_the_lock_file_from_a_custom_intermediate_output_path()
    {
        // Arrange
        var testProject = ChainablePath.Current / "TestCases" / "CustomIntermediatePath";
        (testProject / "artifacts").DeleteFileOrDirectory();

        new DotNetLockFileLoader().GetPackageLockFile((testProject / "CustomIntermediatePath.csproj").ToString());

        var loggingProvider = new InMemoryLoggerProvider();

        var loader = new DotNetLockFileLoader
        {
            Logger = loggingProvider.CreateLogger("")
        };

        // Act
        var lockFile = loader.GetPackageLockFile((testProject / "CustomIntermediatePath.csproj").ToString());

        // Assert
        lockFile.Should().NotBeNull();
        lockFile!.Libraries.Should().Contain(x => x.Name == "Newtonsoft.Json");
        loggingProvider.Logs.Select(x => x.Message).Should().NotContainMatch("*dotnet restore*");
    }

    [TestMethod]
    public void Can_disable_interactive_restores()
    {
        // Arrange
        var loggingProvider = new InMemoryLoggerProvider();

        var testProject = ChainablePath.Current / "TestCases" / "SimpleApp";
        (testProject / "obj").DeleteFileOrDirectory();
        (testProject / "bin").DeleteFileOrDirectory();

        var loader = new DotNetLockFileLoader
        {
            Logger = loggingProvider.CreateLogger(""),
            InteractiveRestore = false
        };

        // Act
        loader.GetPackageLockFile((testProject / "SimpleApp.csproj").ToString());

        // Assert
        loggingProvider.Logs.Select(x => x.Message).Should().NotContainMatch("*--interactive*");
    }
}
