using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.CSharp;

namespace PackageGuard.Specs.CSharp;

[TestClass]
public class NuGetSourceMappingCheckerSpecs
{
    private string directory;

    [TestInitialize]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
    }

    [TestCleanup]
    public void CleanUp() => Directory.Delete(directory, true);

    [TestMethod]
    public void Reports_unmapped_sources_and_their_configuration_files()
    {
        string configPath = WriteConfig(Sources(), "");

        NuGetSourceWarning warning = new NuGetSourceMappingChecker().Check(directory).Should().ContainSingle().Subject;

        warning.PublicSource.Should().Be("public");
        warning.OtherSource.Should().Be("private");
        warning.PublicSourceFile.Should().Be(configPath);
        warning.OtherSourceFile.Should().Be(configPath);
        warning.Pattern.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("*", "*", true)]
    [DataRow("Company.*", "company.*", true)]
    [DataRow("Company.Core", "company.core", true)]
    [DataRow("_Équipe.*", "_équipe.*", true)]
    [DataRow("*", "Company.*", false)]
    [DataRow("Company.*", "Company.Core", false)]
    [DataRow("Public.*", "Company.*", false)]
    public void Applies_nuget_mapping_precedence(string publicPattern, string privatePattern, bool expectedWarning)
    {
        WriteConfig(Sources(), Mapping(publicPattern, privatePattern));

        var warnings = new NuGetSourceMappingChecker().Check(directory);

        warnings.Should().HaveCount(expectedWarning ? 1 : 0);
    }

    [TestMethod]
    public void Exact_id_mappings_on_both_sources_are_reported_even_when_a_prefix_is_also_mapped()
    {
        WriteConfig(Sources(), """
            <packageSourceMapping>
              <packageSource key="public"><package pattern="Company.Core" /><package pattern="Company.Core*" /></packageSource>
              <packageSource key="private"><package pattern="Company.Core" /></packageSource>
            </packageSourceMapping>
            """);

        NuGetSourceWarning warning = new NuGetSourceMappingChecker().Check(directory).Should().ContainSingle().Subject;

        warning.Pattern.Should().Be("Company.Core");
    }

    [TestMethod]
    public void Ignores_a_disabled_source()
    {
        WriteConfig(Sources(), "<disabledPackageSources><add key=\"private\" value=\"true\" /></disabledPackageSources>");

        new NuGetSourceMappingChecker().Check(directory).Should().BeEmpty();
    }

    [TestMethod]
    public void Honors_a_child_clear_and_reports_sources_from_their_actual_files()
    {
        string parent = WriteConfig(Sources(), "");
        string child = Path.Combine(directory, "child");
        Directory.CreateDirectory(child);
        string childConfig = Path.Combine(child, "NuGet.Config");
        File.WriteAllText(childConfig, """
            <configuration><packageSources><add key="another" value="https://example.test/v3/index.json" /></packageSources></configuration>
            """);

        var warnings = new NuGetSourceMappingChecker().Check(child);

        warnings.Should().HaveCount(2);
        warnings.Single(warning => warning.OtherSource == "another").OtherSourceFile.Should().Be(childConfig);
        warnings.Single(warning => warning.OtherSource == "private").OtherSourceFile.Should().Be(parent);

        File.WriteAllText(childConfig, """
            <configuration><packageSources><clear /><add key="private" value="https://example.test/v3/index.json" /></packageSources></configuration>
            """);
        new NuGetSourceMappingChecker().Check(child).Should().BeEmpty();
    }

    [TestMethod]
    public void Reports_effective_mapping_files()
    {
        string configPath = WriteConfig(Sources(), Mapping("Shared.*", "Shared.*"));

        NuGetSourceWarning warning = new NuGetSourceMappingChecker().Check(directory).Should().ContainSingle().Subject;

        warning.Pattern.Should().Be("Shared.*");
        warning.MappingFiles.Should().Contain(configPath);
    }

    [TestMethod]
    public void Uses_effective_mapping_from_multiple_configuration_files()
    {
        string parent = WriteConfig(Sources(), """
            <packageSourceMapping>
              <packageSource key="public"><package pattern="Shared.*" /></packageSource>
            </packageSourceMapping>
            """);
        string child = Path.Combine(directory, "child");
        Directory.CreateDirectory(child);
        string childConfig = Path.Combine(child, "NuGet.Config");
        File.WriteAllText(childConfig, """
            <configuration><packageSourceMapping>
              <packageSource key="private"><package pattern="Shared.*" /></packageSource>
            </packageSourceMapping></configuration>
            """);

        NuGetSourceWarning warning = new NuGetSourceMappingChecker().Check(child).Should().ContainSingle().Subject;

        warning.MappingFiles.Should().Contain(parent).And.Contain(childConfig);
    }

    [TestMethod]
    public void Honors_a_cleared_inherited_mapping()
    {
        WriteConfig(Sources(), Mapping("Shared.*", "Shared.*"));
        string child = Path.Combine(directory, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "NuGet.Config"), """
            <configuration><packageSourceMapping>
              <clear />
              <packageSource key="public"><package pattern="*" /></packageSource>
              <packageSource key="private"><package pattern="Private.*" /></packageSource>
            </packageSourceMapping></configuration>
            """);

        new NuGetSourceMappingChecker().Check(child).Should().BeEmpty();
    }

    [TestMethod]
    public void Reports_the_config_that_cleared_all_inherited_mappings()
    {
        WriteConfig(Sources(), Mapping("Shared.*", "Shared.*"));
        string child = Path.Combine(directory, "child");
        Directory.CreateDirectory(child);
        string childConfig = Path.Combine(child, "NuGet.Config");
        File.WriteAllText(childConfig, """
            <configuration><packageSourceMapping><clear /></packageSourceMapping></configuration>
            """);

        NuGetSourceWarning warning = new NuGetSourceMappingChecker().Check(child).Should().ContainSingle().Subject;

        warning.MappingFiles.Should().Contain(childConfig);
    }

    [TestMethod]
    public void Recognizes_the_nuget_v2_endpoint()
    {
        WriteConfig("""
            <packageSources><clear />
              <add key="public" value="https://www.nuget.org/api/v2/" />
              <add key="private" value="https://private.example.test/v3/index.json" />
            </packageSources>
            """, "");

        new NuGetSourceMappingChecker().Check(directory).Should().ContainSingle();
    }

    [TestMethod]
    public void Finds_shared_ids_outside_ascii_prefixes_owned_by_another_source()
    {
        string coverage = string.Concat("abcdefghijklmnopqrstuvwxyz0123456789_"
            .Select(character => $"<package pattern=\"Shared.{character}*\" />"));
        string mapping = $"""
            <packageSourceMapping>
              <packageSource key="public"><package pattern="Shared.*" /></packageSource>
              <packageSource key="private"><package pattern="Shared.*" /></packageSource>
              <packageSource key="third">{coverage}</packageSource>
            </packageSourceMapping>
            """;
        WriteConfig(Sources(), mapping);

        new NuGetSourceMappingChecker().Check(directory).Should().ContainSingle();
    }

    [TestMethod]
    public void Invalid_shared_prefix_does_not_cause_unbounded_search()
    {
        WriteConfig(Sources(), Mapping("Shared..*", "Shared..*"));

        new NuGetSourceMappingChecker().Check(directory).Should().BeEmpty();
    }

    [TestMethod]
    public void Does_not_identify_a_source_by_its_name_alone()
    {
        WriteConfig("""
            <packageSources><clear />
              <add key="nuget.org" value="https://example.test/v3/index.json" />
              <add key="another" value="https://other.example.test/v3/index.json" />
            </packageSources>
            """, "");

        new NuGetSourceMappingChecker().Check(directory).Should().BeEmpty();
    }

    private string WriteConfig(string sources, string extra)
    {
        string path = Path.Combine(directory, "NuGet.Config");
        File.WriteAllText(path, $"<configuration>{sources}{extra}</configuration>");
        return path;
    }

    private static string Sources() => """
        <packageSources><clear />
          <add key="public" value="https://api.nuget.org/v3/index.json" />
          <add key="private" value="https://private.example.test/v3/index.json" />
        </packageSources>
        """;

    private static string Mapping(string publicPattern, string privatePattern) => $"""
        <packageSourceMapping>
          <packageSource key="public"><package pattern="{publicPattern}" /></packageSource>
          <packageSource key="private"><package pattern="{privatePattern}" /></packageSource>
        </packageSourceMapping>
        """;
}
