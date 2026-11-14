using System.Text.Json;
using PackageGuard.Core.GitHub;
using static PackageGuard.Core.Common.JsonElementReader;
using static PackageGuard.Core.Common.Statistics;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects how many maintainers committed recently and how many of the commits are signed.</summary>
internal sealed class GitHubCommitHealthCollector
{
    private readonly GitHubApiClient apiClient;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubCommitHealthCollector(GitHubApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    /// <summary>Analyzes the commits of the last 12 months for maintainer activity and commit verification ratio.</summary>
    /// <remarks>
    /// The commits are filtered by date here instead of through the <c>since</c> parameter. A <c>since</c> value that
    /// moves with the clock gives the URL a new cache key on every run, so it could never be revalidated.
    /// </remarks>
    public async Task<GitHubCommitHealthData> CollectAsync(GitHubRepositoryContext repository)
    {
        try
        {
            using JsonDocument? commitsDoc = await apiClient.GetJsonAsync(
                $"{repository.ApiRoot}/commits?sha={repository.EscapedDefaultBranch}&per_page=100",
                fields: GitHubResponseFields.Of("author.login", "commit.author.email", "commit.author.date",
                    "commit.verification.verified"));

            if (commitsDoc?.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new GitHubCommitHealthData();
            }

            return Analyze(commitsDoc.RootElement);
        }
        catch
        {
            return new GitHubCommitHealthData();
        }
    }

    /// <summary>Computes the maintainer activity and verification metrics from a listing of commits.</summary>
    private static GitHubCommitHealthData Analyze(JsonElement commits)
    {
        Dictionary<string, DateTimeOffset> lastActivityByMaintainer = [];
        int verifiedCommitCount = 0;
        int signedCommitSampleCount = 0;
        DateTimeOffset oneYearAgo = DateTimeOffset.UtcNow.AddMonths(-12);

        foreach (JsonElement commit in commits.EnumerateArray())
        {
            DateTimeOffset? committedAt = commit.TryGetProperty("commit", out JsonElement commitData) &&
                                          commitData.TryGetProperty("author", out JsonElement commitAuthor)
                ? TryReadDate(commitAuthor, "date")
                : null;

            if (committedAt < oneYearAgo)
            {
                continue;
            }

            string? identity = TryReadIdentity(commit);
            if (string.IsNullOrWhiteSpace(identity) || GitHubIdentity.IsBot(identity))
            {
                continue;
            }

            if (committedAt != null)
            {
                DateTimeOffset activityAt = committedAt.Value;
                if (!lastActivityByMaintainer.TryGetValue(identity, out DateTimeOffset existing) || activityAt > existing)
                {
                    lastActivityByMaintainer[identity] = activityAt;
                }
            }

            bool? verified = TryReadCommitVerification(commit);
            if (verified != null)
            {
                signedCommitSampleCount++;
                if (verified.Value)
                {
                    verifiedCommitCount++;
                }
            }
        }

        List<double> maintainerActivityDays = lastActivityByMaintainer.Values
            .Select(date => (DateTimeOffset.UtcNow - date).TotalDays)
            .ToList();

        int recentMaintainerCount =
            lastActivityByMaintainer.Values.Count(date => date >= DateTimeOffset.UtcNow.AddMonths(-6));

        return new GitHubCommitHealthData
        {
            RecentMaintainerCount = recentMaintainerCount,
            MedianMaintainerActivityDays = ComputeMedian(maintainerActivityDays),
            VerifiedCommitRatio =
                signedCommitSampleCount > 0 ? verifiedCommitCount / (double)signedCommitSampleCount : null
        };
    }

    /// <summary>Reads the GitHub login of the commit author, falling back to the email address in the commit.</summary>
    private static string? TryReadIdentity(JsonElement commit)
    {
        if (commit.TryGetProperty("author", out JsonElement authorElement) &&
            authorElement.TryGetProperty("login", out JsonElement loginElement))
        {
            return loginElement.GetString();
        }

        return commit.TryGetProperty("commit", out JsonElement commitElement) &&
               commitElement.TryGetProperty("author", out JsonElement nestedAuthor) &&
               nestedAuthor.TryGetProperty("email", out JsonElement emailElement)
            ? emailElement.GetString()
            : null;
    }

    /// <summary>Reads the commit.verification.verified boolean from a commit JSON element.</summary>
    private static bool? TryReadCommitVerification(JsonElement commit)
    {
        if (!commit.TryGetProperty("commit", out JsonElement commitElement) ||
            !commitElement.TryGetProperty("verification", out JsonElement verificationElement) ||
            !verificationElement.TryGetProperty("verified", out JsonElement verifiedElement) ||
            verifiedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        return verifiedElement.GetBoolean();
    }
}
