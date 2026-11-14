using System.Text.Json;
using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects how reliably the recent CI workflow runs on a repository's default branch succeed.</summary>
internal sealed class GitHubWorkflowRunCollector
{
    private readonly GitHubApiClient apiClient;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubWorkflowRunCollector(GitHubApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    /// <summary>Fetches recent workflow runs and computes failure/flaky metrics.</summary>
    public async Task<GitHubWorkflowData> CollectAsync(GitHubRepositoryContext repository)
    {
        try
        {
            using JsonDocument? workflowsDoc = await apiClient.GetJsonAsync(
                $"{repository.ApiRoot}/actions/runs?branch={repository.EscapedDefaultBranch}&per_page=10",
                fields: GitHubResponseFields.Of("workflow_runs.status", "workflow_runs.conclusion"));

            if (workflowsDoc is null ||
                !workflowsDoc.RootElement.TryGetProperty("workflow_runs", out JsonElement runs) ||
                runs.ValueKind != JsonValueKind.Array)
            {
                return new GitHubWorkflowData();
            }

            JsonElement[] workflowRuns = runs.EnumerateArray().ToArray();
            int completedRuns = workflowRuns.Count(run => HasValue(run, "status", "completed"));
            int failedRuns = workflowRuns.Count(run => HasValue(run, "conclusion", "failure"));
            bool hasSuccess = workflowRuns.Any(run => HasValue(run, "conclusion", "success"));

            return new GitHubWorkflowData
            {
                RecentFailedWorkflowCount = failedRuns,
                HasRecentSuccessfulWorkflowRun = hasSuccess,
                FailureRate = completedRuns > 0 ? failedRuns / (double)completedRuns : null,
                HasFlakyPattern = completedRuns >= 4 && failedRuns > 0 && hasSuccess && failedRuns < completedRuns
            };
        }
        catch
        {
            return new GitHubWorkflowData();
        }
    }

    /// <summary>Returns true if the property of the workflow run has the expected value, ignoring case.</summary>
    private static bool HasValue(JsonElement run, string propertyName, string expectedValue) =>
        string.Equals(run.TryGetProperty(propertyName, out JsonElement value) ? value.GetString() : null,
            expectedValue, StringComparison.OrdinalIgnoreCase);
}
