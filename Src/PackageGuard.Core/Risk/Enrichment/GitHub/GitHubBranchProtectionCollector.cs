using System.Text.Json;
using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects the protection rules of a repository's default branch.</summary>
internal sealed class GitHubBranchProtectionCollector
{
    private readonly GitHubApiClient apiClient;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubBranchProtectionCollector(GitHubApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    /// <summary>Fetches branch protection settings for the default branch.</summary>
    public async Task<GitHubBranchProtectionData> CollectAsync(GitHubRepositoryContext repository)
    {
        try
        {
            using JsonDocument? branchDoc = await apiClient.GetJsonAsync(
                $"{repository.ApiRoot}/branches/{repository.EscapedDefaultBranch}",
                fields: GitHubResponseFields.Of("protected", "protection.required_status_checks.contexts"));

            if (branchDoc is null)
            {
                return new GitHubBranchProtectionData();
            }

            JsonElement branch = branchDoc.RootElement;
            bool? isProtected = branch.TryGetProperty("protected", out JsonElement protectedElement) &&
                                protectedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? protectedElement.GetBoolean()
                : null;

            return new GitHubBranchProtectionData
            {
                IsProtected = isProtected,
                RequiredStatusCheckCount = TryCountRequiredStatusChecks(branch)
            };
        }
        catch
        {
            return new GitHubBranchProtectionData();
        }
    }

    /// <summary>Counts the required status checks, or returns <see langword="null"/> when none are configured.</summary>
    private static int? TryCountRequiredStatusChecks(JsonElement branch)
    {
        if (branch.TryGetProperty("protection", out JsonElement protectionElement) &&
            protectionElement.TryGetProperty("required_status_checks", out JsonElement checksElement) &&
            checksElement.TryGetProperty("contexts", out JsonElement contextsElement) &&
            contextsElement.ValueKind == JsonValueKind.Array)
        {
            return contextsElement.GetArrayLength();
        }

        return null;
    }
}
