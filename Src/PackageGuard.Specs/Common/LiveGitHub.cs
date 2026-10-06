using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.GitHub;

namespace PackageGuard.Specs.Common;

/// <summary>
/// Helps specs that talk to the live GitHub API.
/// </summary>
internal static class LiveGitHub
{
    /// <summary>
    /// Reports the test as inconclusive when GitHub stopped answering because the rate limit budget ran out.
    /// </summary>
    /// <param name="apiKey">The token the code under test used, which decides which shared client is checked.</param>
    /// <remarks>
    /// These tests talk to the live API. Runners share an IP, so an unauthenticated run competes for 60 requests an
    /// hour with everything else on that address, and CI shares the limit of its token between all builds. Asserting on
    /// signals GitHub declined to hand over would report a spent budget as a defect in the code under test. Call this
    /// after the code under test ran and before asserting on what came from GitHub.
    /// </remarks>
    public static void SkipWhenRefusedToAnswer(string apiKey) =>
        SkipWhenRefusedToAnswer(GitHubApi.GetOrCreateClient(NullLogger.Instance, apiKey));

    /// <summary>
    /// Reports the test as inconclusive when <paramref name="client"/> stopped getting answers because the rate limit
    /// budget ran out.
    /// </summary>
    public static void SkipWhenRefusedToAnswer(GitHubApiClient client)
    {
        if (client.IsExhausted)
        {
            Assert.Inconclusive("Skipped because the GitHub API rate limit is exhausted. Set GITHUB_API_KEY to a " +
                "personal access token to raise the limit from 60 to 5000 requests per hour.");
        }
    }
}
