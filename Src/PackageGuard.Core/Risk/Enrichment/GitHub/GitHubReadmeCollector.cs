using System.Text;
using System.Text.Json;
using PackageGuard.Core.GitHub;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects whether a repository has a README, whether it is boilerplate, and when it last changed.</summary>
internal sealed class GitHubReadmeCollector
{
    private readonly GitHubApiClient apiClient;
    private readonly GitHubRepositoryFiles files;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubReadmeCollector(GitHubApiClient apiClient)
    {
        this.apiClient = apiClient;
        files = new GitHubRepositoryFiles(apiClient);
    }

    /// <summary>Fetches the README and checks if it looks like a boilerplate.</summary>
    public async Task<GitHubReadmeData> CollectAsync(GitHubRepositoryContext repository)
    {
        try
        {
            using JsonDocument? readmeDoc =
                await apiClient.GetJsonAsync($"{repository.ApiRoot}/readme", fields: GitHubResponseFields.Of("content"));
            if (readmeDoc is null)
            {
                return new GitHubReadmeData
                {
                    Exists = false
                };
            }

            return new GitHubReadmeData
            {
                Exists = true,
                IsDefault = LooksLikeBoilerplate(DecodeContent(readmeDoc.RootElement))
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException or DecoderFallbackException)
        {
            return new GitHubReadmeData
            {
                Exists = false
            };
        }
    }

    /// <summary>
    /// Returns the date of the most recent commit to the README in the repository root, or <see langword="null"/>
    /// when the root listing shows no README.
    /// </summary>
    public Task<DateTimeOffset?> GetUpdatedAtAsync(GitHubRepositoryContext repository, string[] rootFiles)
    {
        string? readmeFileName =
            rootFiles.FirstOrDefault(file => file.StartsWith("README", StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(readmeFileName)
            ? Task.FromResult<DateTimeOffset?>(null)
            : files.TryGetLatestCommitDateAsync(repository, readmeFileName);
    }

    /// <summary>Decodes the base64-encoded content of the README response.</summary>
    private static string DecodeContent(JsonElement readme)
    {
        if (readme.TryGetProperty("content", out JsonElement contentElement))
        {
            string? encoded = contentElement.GetString();
            if (!string.IsNullOrWhiteSpace(encoded))
            {
                byte[] bytes = Convert.FromBase64String(encoded.Replace("\n", string.Empty));
                return Encoding.UTF8.GetString(bytes);
            }
        }

        return string.Empty;
    }

    /// <summary>Returns true if the content appears to be a boilerplate README.</summary>
    private static bool LooksLikeBoilerplate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        string normalized = content.ToLowerInvariant();
        return content.Length < 400 ||
               normalized.Contains("todo") ||
               normalized.Contains("coming soon") ||
               normalized.Contains("add a description");
    }
}
