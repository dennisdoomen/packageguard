using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Scaffolding;
using PackageGuard.Core.Policy;
using Pathy;

namespace PackageGuard.Specs.Scaffolding;

[TestClass]
public class ConfigScaffolderSpecs
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
    /// uses, proving the comments and formatting <see cref="ConfigScaffolder"/> emits are actually valid.
    /// </summary>
    private ProjectPolicy ParseGeneratedConfig(string json)
    {
        File.WriteAllText(configPath, json);
        return new ConfigurationLoader(NullLogger.Instance).GetConfigurationFromConfigPath(configPath);
    }

    [TestMethod]
    public void Generated_json_is_parsed_by_the_same_configuration_pipeline_analyze_uses()
    {
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.Proprietary, ["Apache-2.0", "MIT"], [], false);

        ProjectPolicy policy = ParseGeneratedConfig(json);

        policy.AllowList.Licenses.Should().BeEquivalentTo("Apache-2.0", "MIT");
    }

    [TestMethod]
    public void Generated_json_parses_cleanly_with_an_empty_allow_list()
    {
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.Proprietary, [], [], false);

        ProjectPolicy policy = ParseGeneratedConfig(json);

        policy.AllowList.Licenses.Should().BeEmpty();
    }

    [TestMethod]
    public void Includes_the_preset_name_as_a_comment()
    {
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.Saas, ["MIT"], [], false);

        json.Should().Contain("no-network-copyleft");
    }

    [TestMethod]
    public void Explains_copyleft_for_the_reader()
    {
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.Proprietary, ["MIT"], [], false);

        json.Should().Contain("Copyleft");
    }

    [TestMethod]
    public void Generated_json_with_warn_licenses_parses_into_the_warn_list()
    {
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.Saas, ["LGPL-2.1-only", "MIT"], ["LGPL-2.1-only"], false);

        ProjectPolicy policy = ParseGeneratedConfig(json);

        policy.AllowList.Licenses.Should().BeEquivalentTo("LGPL-2.1-only", "MIT");
        policy.WarnList.Licenses.Should().BeEquivalentTo("LGPL-2.1-only");
    }

    [TestMethod]
    public void Generated_json_with_risk_gates_parses_into_risk_deny_rules()
    {
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.Proprietary, ["MIT"], [], true);

        ProjectPolicy policy = ParseGeneratedConfig(json);

        policy.DenyList.MaxOverallRisk.Should().Be(60);
        policy.DenyList.MaxSecurityRisk.Should().Be(7);
        policy.DenyList.MaxOsvSeverityScore.Should().Be(7.0);
        policy.DenyList.DenyDeprecated.Should().BeTrue();
        policy.DenyList.MinPackageAgeDays.Should().Contain("npm", 14).And.Contain("nuget", 3);
        policy.AllowList.Licenses.Should().BeEquivalentTo("MIT");
    }

    [TestMethod]
    public void Generated_json_without_risk_gates_has_no_risk_rules()
    {
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.Proprietary, ["MIT"], [], false);

        ProjectPolicy policy = ParseGeneratedConfig(json);

        policy.DenyList.HasRiskPolicies.Should().BeFalse();
        json.Should().NotContain("maxOverallRisk");
    }

    [TestMethod]
    public void Mentions_risk_exceptions_only_as_a_comment()
    {
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.Proprietary, ["MIT"], [], true);

        ParseGeneratedConfig(json).RiskExceptions.Should().BeEmpty();
        json.Should().Contain("riskExceptions");
    }
}
