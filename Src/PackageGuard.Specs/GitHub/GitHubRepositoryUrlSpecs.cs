using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.GitHub;

namespace PackageGuard.Specs.GitHub;

[TestClass]
public class GitHubRepositoryUrlSpecs
{
    [TestMethod]
    [DataRow("https://github.com/dennisdoomen/packageguard", "dennisdoomen/packageguard")]
    [DataRow("https://github.com/dennisdoomen/packageguard.git", "dennisdoomen/packageguard")]
    [DataRow("git+https://github.com/lodash/lodash.git", "lodash/lodash")]
    [DataRow("https://github.com/acme/repo./tree/main", "acme/repo")]
    public void Extracts_the_owner_and_repository_name(string url, string expected)
    {
        GitHubRepositoryUrl.TryGetIdentifier(url).Should().Be(expected);
    }

    [TestMethod]
    public void Only_removes_a_git_suffix_and_not_git_inside_the_name()
    {
        GitHubRepositoryUrl.TryGetIdentifier("https://github.com/acme/acme.github.io")
            .Should().Be("acme/acme.github.io");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("https://gitlab.com/acme/repo")]
    public void Ignores_urls_that_do_not_point_to_github(string url)
    {
        GitHubRepositoryUrl.TryGetIdentifier(url).Should().BeNull();
        GitHubRepositoryUrl.TryGetWebRoot(url).Should().BeNull();
        GitHubRepositoryUrl.TryGetApiRoot(url).Should().BeNull();
    }

    [TestMethod]
    public void Builds_the_web_and_api_roots()
    {
        const string url = "git+https://github.com/lodash/lodash.git";

        GitHubRepositoryUrl.TryGetWebRoot(url).Should().Be("https://github.com/lodash/lodash");
        GitHubRepositoryUrl.TryGetApiRoot(url).Should().Be("https://api.github.com/repos/lodash/lodash");
    }
}
