using System.Text.Json;
using System.Text.RegularExpressions;

namespace PackageGuard.Core.FetchingStrategies;

/// <summary>
/// Fetches licenses using GitHub metadata.
/// </summary>
public class GitHubLicenseFetcher : IFetchLicense
{
    private static readonly HttpClient HttpClient = new();

    static GitHubLicenseFetcher()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("PackageGuard", "v1"));
    }

    public async Task FetchLicenseAsync(PackageInfo package)
    {
        if (package.RepositoryUrl is not null)
        {
            string? url = GetGitHubLicenseUrl(package.RepositoryUrl);

            if (url is not null)
            {
                string licenseJson = await HttpClient.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(licenseJson);
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
