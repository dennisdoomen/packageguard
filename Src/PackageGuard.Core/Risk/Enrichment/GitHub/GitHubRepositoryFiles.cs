using System.Text;
using System.Text.Json;
using PackageGuard.Core.GitHub;
using static PackageGuard.Core.Common.JsonElementReader;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Reads the files of a GitHub repository through the contents and commits APIs.</summary>
internal sealed class GitHubRepositoryFiles
{
    private readonly GitHubApiClient apiClient;

    /// <summary>Creates a reader that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubRepositoryFiles(GitHubApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    /// <summary>Lists filenames in the repository root directory.</summary>
    public async Task<string[]> GetRootFilesAsync(GitHubRepositoryContext repository)
    {
        using JsonDocument? contents = await apiClient.GetJsonAsync(
            $"{repository.ApiRoot}/contents?ref={repository.EscapedDefaultBranch}",
            fields: GitHubResponseFields.Of("name"));

        if (contents?.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return contents.RootElement.EnumerateArray()
            .Select(item => TryReadString(item, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    /// <summary>Fetches and decodes the base64-encoded content of a file via the GitHub contents API.</summary>
    public async Task<string> TryGetContentAsync(GitHubRepositoryContext repository, string path)
    {
        try
        {
            using JsonDocument? fileDoc = await apiClient.GetJsonAsync(
                $"{repository.ApiRoot}/contents/{Uri.EscapeDataString(path)}?ref={repository.EscapedDefaultBranch}",
                fields: GitHubResponseFields.Of("content"));

            if (fileDoc is not null && fileDoc.RootElement.TryGetProperty("content", out JsonElement contentElement))
            {
                string? encoded = contentElement.GetString();
                if (!string.IsNullOrWhiteSpace(encoded))
                {
                    byte[] bytes = Convert.FromBase64String(encoded.Replace("\n", string.Empty));
                    return Encoding.UTF8.GetString(bytes);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException)
        {
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns the date of the most recent commit that touched the given file path. This only refines a signal, so
    /// the request is dropped once the rate limit budget runs low.
    /// </summary>
    public async Task<DateTimeOffset?> TryGetLatestCommitDateAsync(GitHubRepositoryContext repository, string path)
    {
        try
        {
            using JsonDocument? commitsDoc = await apiClient.GetJsonAsync(
                $"{repository.ApiRoot}/commits?sha={repository.EscapedDefaultBranch}" +
                $"&path={Uri.EscapeDataString(path)}&per_page=1",
                GitHubRequestImportance.Optional,
                GitHubResponseFields.Of("commit.author.date"));

            if (commitsDoc?.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement commit = commitsDoc.RootElement.EnumerateArray().FirstOrDefault();
            return commit.ValueKind == JsonValueKind.Undefined
                ? null
                : TryReadDate(commit.GetProperty("commit").GetProperty("author"), "date");
        }
        catch
        {
            return null;
        }
    }
}
