using System.IO;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pathy;

namespace PackageGuard.Specs;

[TestClass]
public class InitCommandConfigPathSpecs
{
    private ChainablePath directory;

    [TestInitialize]
    public void Setup()
    {
        directory = ChainablePath.Temp / Path.GetRandomFileName();
        directory.CreateDirectoryRecursively();
    }

    [TestCleanup]
    public void Cleanup()
    {
        directory.DeleteFileOrDirectory();
    }

    [TestMethod]
    public void Writes_the_configuration_next_to_the_solution_file()
    {
        File.WriteAllText(directory / "My.sln", "");
        string project = directory / "src" / "App";
        Directory.CreateDirectory(project);

        var path = InitCommand.ResolveConfigPath(new InitCommandSettings { ProjectPath = project });

        path.ToString().Should().Be(directory / ".packageguard" / "config.json");
    }

    [TestMethod]
    public void Falls_back_to_the_project_directory_when_there_is_no_solution_file()
    {
        var path = InitCommand.ResolveConfigPath(new InitCommandSettings { ProjectPath = directory });

        path.ToString().Should().Be(directory / ".packageguard" / "config.json");
    }

    [TestMethod]
    public void Uses_the_explicit_config_path_when_one_is_given()
    {
        string explicitPath = directory / "custom.json";

        var path = InitCommand.ResolveConfigPath(new InitCommandSettings { ConfigPath = explicitPath });

        path.ToString().Should().Be(explicitPath);
    }
}
