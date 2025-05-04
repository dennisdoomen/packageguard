using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

public sealed class LicenseFetcher(ILogger logger)
{
    private static readonly HttpClient HttpClient = new();

    static LicenseFetcher()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PackageGuard", "v1"));
    }

    public async Task AmendWithMissingLicenseInformation(PackageInfo package)
    {
        if (package.License is null)
        {
            logger.LogInformation("Package {Name} {Version} does not have a license. Fetching it separately",
                package.Id, package.Version);

            await TryFetchLicenseFromGitHubMetaData(package);
        }

        if (package.License is null)
        {
            await TryFetchLicenseFromDownloadUrl(package);
        }

        logger.LogInformation("Determined the license to be {License}", package.License);
    }

    private async Task TryFetchLicenseFromGitHubMetaData(PackageInfo package)
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

    private async Task TryFetchLicenseFromDownloadUrl(PackageInfo package)
    {
        package.License = "Unknown";

        if (package.LicenseUrl is { Length: > 0 })
        {
            try
            {
                string licenseText = await HttpClient.GetStringAsync(package.LicenseUrl);

                if (licenseText.Contains("MIT license", StringComparison.OrdinalIgnoreCase))
                {
                    package.License = "MIT";
                }
                else if (licenseText.Contains("Apache License", StringComparison.OrdinalIgnoreCase))
                {
                    package.License = "Apache-2.0";
                }
                else if (licenseText.Contains("GNU General Public License", StringComparison.OrdinalIgnoreCase))
                {
                    package.License = "GPL-3.0";
                }
                else
                {
                    logger.LogWarning("Did not detect any well-known licenses in {URL}", package.LicenseUrl);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Failed to extract the license from URL {URL}: {ErrorCode}", package.LicenseUrl, ex.Message);
            }
        }
    }
}
