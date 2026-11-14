using System.Text.Json;
using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects how many people contribute to a repository and how concentrated their contributions are.</summary>
internal sealed class GitHubContributorCollector
{
    private readonly GitHubApiClient apiClient;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubContributorCollector(GitHubApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    /// <summary>Fetches contributor list and computes concentration metrics.</summary>
    public async Task<GitHubContributorData> CollectAsync(GitHubRepositoryContext repository)
    {
        using JsonDocument? contributorsDoc = await apiClient.GetJsonAsync(
            $"{repository.ApiRoot}/contributors?per_page=100",
            fields: GitHubResponseFields.Of("login", "contributions"));
        if (contributorsDoc?.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new GitHubContributorData();
        }

        int[] contributionCounts = contributorsDoc.RootElement.EnumerateArray()
            .Where(IsHuman)
            .Select(contributor => contributor.TryGetProperty("contributions", out JsonElement contributionsElement) &&
                                   contributionsElement.TryGetInt32(out int contributions)
                ? contributions
                : 0)
            .OrderByDescending(contributions => contributions)
            .ToArray();

        double totalContributions = contributionCounts.Sum();
        return new GitHubContributorData
        {
            ContributorCount = contributionCounts.Length,
            TopContributorShare =
                totalContributions > 0 ? contributionCounts.FirstOrDefault() / totalContributions : null,
            TopTwoContributorShare =
                totalContributions > 0 ? contributionCounts.Take(2).Sum() / totalContributions : null
        };
    }

    /// <summary>
    /// Returns true unless the contributor's login marks it as a bot. Unlike <see cref="GitHubIdentity.IsBot"/>, a
    /// contributor without a login counts as a human.
    /// </summary>
    private static bool IsHuman(JsonElement contributor)
    {
        string login = contributor.TryGetProperty("login", out JsonElement loginElement)
            ? loginElement.GetString() ?? string.Empty
            : string.Empty;

        return !login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) &&
               !login.Contains("dependabot", StringComparison.OrdinalIgnoreCase) &&
               !login.Contains("copilot", StringComparison.OrdinalIgnoreCase);
    }
}
