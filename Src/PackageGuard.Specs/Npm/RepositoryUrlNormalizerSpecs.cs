using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Npm;

namespace PackageGuard.Specs.Npm;

[TestClass]
public class RepositoryUrlNormalizerSpecs
{
    [TestMethod]
    [DataRow("git+https://github.com/lodash/lodash.git", "https://github.com/lodash/lodash")]
    [DataRow("git+ssh://git@github.com/mozilla/source-map.git", "https://github.com/mozilla/source-map")]
    [DataRow("ssh://git@github.com/mozilla/source-map", "https://github.com/mozilla/source-map")]
    [DataRow("ssh://git@github.com:22/mozilla/source-map.git", "https://github.com/mozilla/source-map")]
    [DataRow("git://github.com/user/repo.git", "https://github.com/user/repo")]
    [DataRow("git@github.com:user/repo.git", "https://github.com/user/repo")]
    [DataRow("github:user/repo", "https://github.com/user/repo")]
    [DataRow("gitlab:user/repo", "https://gitlab.com/user/repo")]
    [DataRow("user/repo", "https://github.com/user/repo")]
    [DataRow("https://github.com/user/repo/", "https://github.com/user/repo")]
    [DataRow("https://github.com/user/repo#readme", "https://github.com/user/repo")]
    public void Turns_a_repository_description_into_a_browsable_https_url(string declared, string expected)
    {
        RepositoryUrlNormalizer.Normalize(declared).Should().Be(expected);
    }

    [TestMethod]
    [DataRow("https://github.com/acme/digit", "https://github.com/acme/digit")]
    [DataRow("https://github.com/acme/git", "https://github.com/acme/git")]
    public void Only_removes_a_git_suffix_and_not_trailing_letters_of_the_name(string declared, string expected)
    {
        RepositoryUrlNormalizer.Normalize(declared).Should().Be(expected);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public void Ignores_a_missing_repository(string declared)
    {
        RepositoryUrlNormalizer.Normalize(declared).Should().BeNull();
    }
}
