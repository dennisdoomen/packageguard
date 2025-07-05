using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PackageGuard.Core.FetchingStrategies;

/// <summary>
/// Fetches licenses using GitHub metadata and an optional GitHub API key to prevent rate limiting.
/// </summary>
public class GitHubLicenseFetcher(string? gitHubApiKey) : IFetchLicense
{
    private static readonly HttpClient HttpClient = new();

    static GitHubLicenseFetcher()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PackageGuard", "v1"));
    }

    public async Task FetchLicenseAsync(PackageInfo package)
    {
        if (package.RepositoryUrl is not null)
        {
            string? url = GetGitHubLicenseUrl(package.RepositoryUrl);

            if (url is not null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (gitHubApiKey is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gitHubApiKey);
                }

                using HttpResponseMessage response = await HttpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                package.License = doc.RootElement.GetProperty("license").GetProperty("spdx_id").GetString();

                if (package.License?.Equals("noassertion", StringComparison.OrdinalIgnoreCase) == true)
                {
                    package.License = null;
                }
            }
        }
    }

    private string? GetGitHubLicenseUrl(string repositoryUrl)
    {
        string? url = null;

        const string validCharacters = "[a-zA-Z0-9._-]";

        var match = Regex.Match(repositoryUrl,
            $@"raw.githubusercontent.com\/(?<owner>{validCharacters}+?)\/(?<repo>{validCharacters}+)");

        if (match.Length == 0)
        {
            match = Regex.Match(repositoryUrl,
                $@"github.com\/(?<owner>{validCharacters}+?)\/(?<repo>{validCharacters}+)");
        }

        if (match.Length > 0)
        {
            string owner = match.Groups["owner"].Value;
            string repo = match.Groups["repo"].Value;

            url = $"https://api.github.com/repos/{owner}/{repo}/license";
        }

        return url;
    }
}
