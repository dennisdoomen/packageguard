using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PackageGuard.Specs;

[TestClass]
public class PackageNameMatcherSpecs
{
    private static readonly string[] Names =
    [
        "Newtonsoft.Json",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Logging.Abstractions",
        "FluentAssertions"
    ];

    [TestMethod]
    public void Resolves_an_exact_case_insensitive_match()
    {
        // Act
        PackageNameMatch match = PackageNameMatcher.Resolve("newtonsoft.json", Names);

        // Assert
        match.MatchedName.Should().Be("Newtonsoft.Json");
    }

    [TestMethod]
    public void Resolves_a_single_partial_match()
    {
        // Act
        PackageNameMatch match = PackageNameMatcher.Resolve("Fluent", Names);

        // Assert
        match.MatchedName.Should().Be("FluentAssertions");
    }

    [TestMethod]
    public void Suggests_candidates_for_an_ambiguous_partial_match()
    {
        // Act
        PackageNameMatch match = PackageNameMatcher.Resolve("Microsoft.Extensions", Names);

        // Assert
        match.MatchedName.Should().BeNull();
        match.Suggestions.Should().Contain("Microsoft.Extensions.Logging");
        match.Suggestions.Should().Contain("Microsoft.Extensions.Logging.Abstractions");
    }

    [TestMethod]
    public void Suggests_close_matches_for_a_misspelled_name()
    {
        // Act
        PackageNameMatch match = PackageNameMatcher.Resolve("Newtonsft.Jso", Names);

        // Assert
        match.MatchedName.Should().Be("Newtonsoft.Json");
    }

    [TestMethod]
    public void Returns_no_suggestions_for_a_completely_unrelated_name()
    {
        // Act
        PackageNameMatch match = PackageNameMatcher.Resolve("Xyzzy1234NotEvenClose", Names);

        // Assert
        match.MatchedName.Should().BeNull();
        match.Suggestions.Should().BeEmpty();
    }
}
