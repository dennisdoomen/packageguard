using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Init;
using PackageGuard.Core.Policy;
using Pathy;

namespace PackageGuard.Specs.Init;

[TestClass]
public class InitConfigWriterSpecs
{
    private ChainablePath configPath;

    [TestInitialize]
    public void Setup()
    {
        configPath = ChainablePath.Temp / Path.GetRandomFileName();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (configPath.IsFile)
        {
            File.Delete(configPath);
        }
    }

    /// <summary>
    /// Parses the generated JSON through the same <see cref="ConfigurationLoader"/> the <c>analyze</c> command
    /// uses, proving the comments and formatting <see cref="InitConfigWriter"/> emits are actually valid.
    /// </summary>
    private ProjectPolicy ParseGeneratedConfig(string json)
    {
        File.WriteAllText(configPath, json);
        return new ConfigurationLoader(NullLogger.Instance).GetConfigurationFromConfigPath(configPath);
    }

    [TestMethod]
    public void Generated_json_is_parsed_by_the_same_configuration_pipeline_analyze_uses()
    {
        string json = InitConfigWriter.BuildConfigJson(SoftwareProfile.Proprietary, ["Apache-2.0", "MIT"]);

        ProjectPolicy policy = ParseGeneratedConfig(json);

        policy.AllowList.Licenses.Should().BeEquivalentTo("Apache-2.0", "MIT");
    }

    [TestMethod]
    public void Generated_json_parses_cleanly_with_an_empty_allow_list()
    {
        string json = InitConfigWriter.BuildConfigJson(SoftwareProfile.Proprietary, []);

        ProjectPolicy policy = ParseGeneratedConfig(json);

        policy.AllowList.Licenses.Should().BeEmpty();
    }

    [TestMethod]
    public void Includes_the_preset_name_as_a_comment()
    {
        string json = InitConfigWriter.BuildConfigJson(SoftwareProfile.Saas, ["MIT"]);

        json.Should().Contain("no-network-copyleft");
    }

    [TestMethod]
    public void Explains_copyleft_for_the_reader()
    {
        string json = InitConfigWriter.BuildConfigJson(SoftwareProfile.Proprietary, ["MIT"]);

        json.Should().Contain("Copyleft");
    }
}
