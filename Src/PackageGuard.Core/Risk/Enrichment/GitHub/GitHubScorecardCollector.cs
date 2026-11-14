using System.Text.Json;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.Common;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects the OpenSSF Scorecard results of a GitHub repository.</summary>
internal sealed class GitHubScorecardCollector
{
    /// <summary>Client for the OpenSSF Scorecard API, which is not subject to the GitHub rate limit.</summary>
    private static readonly HttpClient ScorecardHttpClient = PackageGuardHttpClient.Create(TimeSpan.FromSeconds(20));

    private readonly ILogger logger;

    /// <summary>Creates a collector that reports the requests it sends to the given logger.</summary>
    /// <param name="logger">The logger to report requests to.</param>
    public GitHubScorecardCollector(ILogger logger)
    {
        this.logger = logger;
    }

    /// <summary>Fetches OpenSSF Scorecard data from the security scorecard API.</summary>
    public async Task<GitHubScorecardData> CollectAsync(GitHubRepositoryContext repository)
    {
        try
        {
            string scorecardUrl =
                $"https://api.securityscorecards.dev/projects/github.com/{repository.OwnerLogin}/{repository.RepositoryName}";
            logger.LogDebug("GET {Url}", scorecardUrl);
            using HttpResponseMessage response = await ScorecardHttpClient.GetAsync(scorecardUrl);
            response.EnsureSuccessStatusCode();

            using JsonDocument scorecardDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return Read(scorecardDoc.RootElement);
        }
        catch
        {
            return new GitHubScorecardData();
        }
    }

    /// <summary>Reads the aggregate score and the checks PackageGuard uses from a Scorecard response.</summary>
    private static GitHubScorecardData Read(JsonElement root)
    {
        bool? hasBranchProtection = null;
        double? binaryArtifactsScore = null;
        if (root.TryGetProperty("checks", out JsonElement checks) && checks.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement check in checks.EnumerateArray())
            {
                string? name = check.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : null;
                if (string.Equals(name, "Branch-Protection", StringComparison.OrdinalIgnoreCase) &&
                    check.TryGetProperty("score", out JsonElement branchScoreElement) &&
                    branchScoreElement.TryGetDouble(out double branchScore))
                {
                    hasBranchProtection = branchScore > 0;
                }

                if (string.Equals(name, "Binary-Artifacts", StringComparison.OrdinalIgnoreCase) &&
                    check.TryGetProperty("score", out JsonElement binaryScoreElement) &&
                    binaryScoreElement.TryGetDouble(out double binaryScore))
                {
                    binaryArtifactsScore = binaryScore;
                }
            }
        }

        return new GitHubScorecardData
        {
            Score = root.TryGetProperty("score", out JsonElement score) && score.TryGetDouble(out double value) ? value : null,
            HasBranchProtection = hasBranchProtection,
            BinaryArtifactsScore = binaryArtifactsScore
        };
    }
}
