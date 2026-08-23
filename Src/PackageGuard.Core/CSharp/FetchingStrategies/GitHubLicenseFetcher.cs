using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.CSharp.FetchingStrategies;

/// <summary>
/// Fetches licenses using GitHub metadata and an optional GitHub API key to prevent rate limiting.
/// </summary>
public class GitHubLicenseFetcher : IFetchLicense
{
    private readonly ILogger logger;
    private readonly GitHubApiClient apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubLicenseFetcher"/> class.
    /// </summary>
    /// <param name="logger">The logger to report fetch problems to.</param>
    /// <param name="gitHubApiKey">The GitHub personal access token to authenticate with, if any.</param>
    public GitHubLicenseFetcher(ILogger logger, string? gitHubApiKey)
        : this(logger, GitHubApi.GetOrCreateClient(logger, gitHubApiKey))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubLicenseFetcher"/> class that shares the given client.
    /// </summary>
    /// <param name="logger">The logger to report fetch problems to.</param>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    internal GitHubLicenseFetcher(ILogger logger, GitHubApiClient apiClient)
    {
        this.logger = logger;
        this.apiClient = apiClient;
    }

    /// <summary>
    /// Resolves the SPDX identifier of the package's license from the license endpoint of its GitHub repository.
    /// </summary>
    /// <param name="package">The package to amend with license information.</param>
    public async Task FetchLicenseAsync(PackageInfo package)
    {
        string? url = GetGitHubLicenseUrl(package.RepositoryUrl);
        if (url is null)
        {
            return;
        }

        logger.LogDebug("Fetching GitHub license from {Url}", url);
        using JsonDocument? document = await apiClient.GetJsonAsync(url);
        if (document is null)
        {
            return;
        }

        package.License = ReadSpdxIdentifier(document);
        if (package.License is not null)
        {
            package.LicenseEvidence = LicenseEvidence.Concluded;
        }
    }

    /// <summary>
    /// Reads the SPDX identifier from a GitHub license response, treating GitHub's "no assertion" answer as unknown.
    /// </summary>
    private static string? ReadSpdxIdentifier(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("license", out JsonElement license) ||
            !license.TryGetProperty("spdx_id", out JsonElement spdxId))
        {
            return null;
        }

        string? identifier = spdxId.GetString();
        return identifier?.Equals("noassertion", StringComparison.OrdinalIgnoreCase) == true ? null : identifier;
    }

    /// <summary>
    /// Builds the GitHub license endpoint URL for a repository URL, or returns <see langword="null"/> when the URL
    /// does not point at GitHub.
    /// </summary>
    private static string? GetGitHubLicenseUrl(string? repositoryUrl)
    {
        if (repositoryUrl is null)
        {
            return null;
        }

        const string validCharacters = "[a-zA-Z0-9._-]";

        var match = Regex.Match(repositoryUrl,
            $@"raw.githubusercontent.com\/(?<owner>{validCharacters}+?)\/(?<repo>{validCharacters}+)");

        if (match.Length == 0)
        {
            match = Regex.Match(repositoryUrl,
                $@"github.com\/(?<owner>{validCharacters}+?)\/(?<repo>{validCharacters}+)");
        }

        if (match.Length == 0)
        {
            return null;
        }

        return $"https://api.github.com/repos/{match.Groups["owner"].Value}/{match.Groups["repo"].Value}/license";
    }
}
