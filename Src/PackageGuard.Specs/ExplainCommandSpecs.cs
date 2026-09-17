using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Package;

namespace PackageGuard.Specs;

[TestClass]
public class ExplainCommandSpecs
{
    private static PackageInfo CreatePackage(string name, string version)
    {
        return new PackageInfo { Name = name, Version = version };
    }

    [TestMethod]
    public void Resolves_a_single_version_of_an_exactly_named_package()
    {
        PackageInfo[] packages = [CreatePackage("Newtonsoft.Json", "13.0.3")];
        var settings = new ExplainCommandSettings { PackageName = "Newtonsoft.Json" };

        ExplainCommand.ExplainTarget target = ExplainCommand.ResolveTarget(settings, packages);

        target.MatchedName.Should().Be("Newtonsoft.Json");
        target.Package.Should().NotBeNull();
        target.Package!.Version.Should().Be("13.0.3");
        target.AmbiguousVersions.Should().BeEmpty();
    }

    [TestMethod]
    public void Resolves_a_partially_typed_name_to_its_single_match()
    {
        PackageInfo[] packages = [CreatePackage("Newtonsoft.Json", "13.0.3")];
        var settings = new ExplainCommandSettings { PackageName = "Newtonsoft" };

        ExplainCommand.ExplainTarget target = ExplainCommand.ResolveTarget(settings, packages);

        target.MatchedName.Should().Be("Newtonsoft.Json");
        target.Package.Should().NotBeNull();
    }

    [TestMethod]
    public void Resolves_a_misspelled_name_when_only_one_candidate_is_close_enough()
    {
        PackageInfo[] packages = [CreatePackage("Newtonsoft.Json", "13.0.3")];
        var settings = new ExplainCommandSettings { PackageName = "Newtonsft.Jso" };

        ExplainCommand.ExplainTarget target = ExplainCommand.ResolveTarget(settings, packages);

        target.MatchedName.Should().Be("Newtonsoft.Json", "the misspelling should still resolve to the only close match");
        target.Package.Should().NotBeNull();
    }

    [TestMethod]
    public void Reports_no_match_and_suggestions_for_an_ambiguous_partial_query()
    {
        PackageInfo[] packages =
        [
            CreatePackage("Microsoft.Extensions.Logging", "1.0.0"),
            CreatePackage("Microsoft.Extensions.Logging.Abstractions", "1.0.0")
        ];
        var settings = new ExplainCommandSettings { PackageName = "Microsoft.Extensions" };

        ExplainCommand.ExplainTarget target = ExplainCommand.ResolveTarget(settings, packages);

        target.Package.Should().BeNull();
        target.MatchedName.Should().BeNull();
        target.Suggestions.Should().Contain(["Microsoft.Extensions.Logging", "Microsoft.Extensions.Logging.Abstractions"]);
    }

    [TestMethod]
    public void Reports_no_match_at_all_for_a_completely_unrelated_query()
    {
        PackageInfo[] packages = [CreatePackage("Newtonsoft.Json", "13.0.3")];
        var settings = new ExplainCommandSettings { PackageName = "Xyzzy1234NotEvenClose" };

        ExplainCommand.ExplainTarget target = ExplainCommand.ResolveTarget(settings, packages);

        target.Package.Should().BeNull();
        target.MatchedName.Should().BeNull();
        target.Suggestions.Should().BeEmpty();
    }

    [TestMethod]
    public void Reports_every_version_as_ambiguous_when_no_version_was_requested()
    {
        PackageInfo[] packages =
        [
            CreatePackage("Newtonsoft.Json", "12.0.3"),
            CreatePackage("Newtonsoft.Json", "13.0.3")
        ];
        var settings = new ExplainCommandSettings { PackageName = "Newtonsoft.Json" };

        ExplainCommand.ExplainTarget target = ExplainCommand.ResolveTarget(settings, packages);

        target.Package.Should().BeNull();
        target.MatchedName.Should().Be("Newtonsoft.Json");
        target.AmbiguousVersions.Should().HaveCount(2);
        target.AvailableVersions.Should().BeEquivalentTo("12.0.3", "13.0.3");
    }

    [TestMethod]
    public void Resolves_the_requested_version_among_several_candidates()
    {
        PackageInfo[] packages =
        [
            CreatePackage("Newtonsoft.Json", "12.0.3"),
            CreatePackage("Newtonsoft.Json", "13.0.3")
        ];
        var settings = new ExplainCommandSettings { PackageName = "Newtonsoft.Json", Version = "13.0.3" };

        ExplainCommand.ExplainTarget target = ExplainCommand.ResolveTarget(settings, packages);

        target.Package.Should().NotBeNull();
        target.Package!.Version.Should().Be("13.0.3");
        target.AmbiguousVersions.Should().BeEmpty();
    }

    [TestMethod]
    public void Reports_the_available_versions_when_the_requested_version_does_not_exist()
    {
        PackageInfo[] packages =
        [
            CreatePackage("Newtonsoft.Json", "12.0.3"),
            CreatePackage("Newtonsoft.Json", "13.0.3")
        ];
        var settings = new ExplainCommandSettings { PackageName = "Newtonsoft.Json", Version = "9.9.9" };

        ExplainCommand.ExplainTarget target = ExplainCommand.ResolveTarget(settings, packages);

        target.Package.Should().BeNull();
        target.AmbiguousVersions.Should().BeEmpty();
        target.AvailableVersions.Should().BeEquivalentTo("12.0.3", "13.0.3");
    }
}
