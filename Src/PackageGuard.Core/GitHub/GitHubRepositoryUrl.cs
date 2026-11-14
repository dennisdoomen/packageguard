using System.Text.RegularExpressions;

namespace PackageGuard.Core.GitHub;

/// <summary>
/// Extracts the owner and repository name from a GitHub repository URL and builds the related web and API URLs.
/// </summary>
internal static class GitHubRepositoryUrl
{
    private static readonly Regex OwnerAndRepositoryPattern = new(
        @"github\.com/(?<owner>[a-zA-Z0-9._-]+)/(?<repo>[a-zA-Z0-9._-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extracts the <c>&lt;owner&gt;/&lt;repo&gt;</c> identifier from <paramref name="repositoryUrl"/>,
    /// or returns <see langword="null"/> if it is not a GitHub URL.
    /// </summary>
    public static string? TryGetIdentifier(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return null;
        }

        Match match = OwnerAndRepositoryPattern.Match(repositoryUrl);
        if (!match.Success)
        {
            return null;
        }

        string repositoryName = match.Groups["repo"].Value.TrimEnd('.');
        if (repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repositoryName = repositoryName[..^4];
        }

        return $"{match.Groups["owner"].Value}/{repositoryName}";
    }

    /// <summary>
    /// Returns the <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;</c> URL for <paramref name="repositoryUrl"/>,
    /// or <see langword="null"/> if it is not a GitHub URL.
    /// </summary>
    public static string? TryGetWebRoot(string? repositoryUrl)
    {
        string? identifier = TryGetIdentifier(repositoryUrl);
        return identifier is null ? null : $"https://github.com/{identifier}";
    }

    /// <summary>
    /// Returns the <c>https://api.github.com/repos/&lt;owner&gt;/&lt;repo&gt;</c> URL for <paramref name="repositoryUrl"/>,
    /// or <see langword="null"/> if it is not a GitHub URL.
    /// </summary>
    public static string? TryGetApiRoot(string? repositoryUrl)
    {
        string? identifier = TryGetIdentifier(repositoryUrl);
        return identifier is null ? null : $"https://api.github.com/repos/{identifier}";
    }
}
