using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.ProjectModel;
using PackageGuard.Core.CSharp;

namespace PackageGuard.Specs;

[TestClass]
public class LockFileCollectorSpecs
{
    private const string EmptyAssetsFile = """{ "version": 3, "targets": {}, "libraries": {}, "projectFileDependencyGroups": {} }""";

    private string directory = "";

    [TestInitialize]
    public void CreateDirectory()
    {
        directory = Path.Combine(Path.GetTempPath(), "packageguard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(directory, recursive: true);

    [TestMethod]
    public void Can_load_the_lock_file_of_a_project_without_restoring()
    {
        // Arrange
        string project = CreateProject("Api", EmptyAssetsFile);

        // Act
        var lockFiles = new LockFileCollector { SkipRestore = true }.Collect(project);

        // Assert
        lockFiles.Should().ContainSingle();
    }

    [TestMethod]
    public void Skips_a_project_that_has_no_lock_file()
    {
        // Arrange
        string project = CreateProject("Api", assetsFile: null);

        // Act
        IReadOnlyCollection<LockFile> lockFiles = new LockFileCollector { SkipRestore = true }.Collect(project);

        // Assert
        lockFiles.Should().BeEmpty();
    }

    private string CreateProject(string name, string assetsFile)
    {
        string project = Path.Combine(directory, name + ".csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        if (assetsFile is not null)
        {
            Directory.CreateDirectory(Path.Combine(directory, "obj"));
            File.WriteAllText(Path.Combine(directory, "obj", "project.assets.json"), assetsFile);
        }

        return project;
    }
}
