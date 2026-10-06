namespace PackageGuard.Core.Npm;

/// <summary>
/// Turns the many ways a package.json can describe its repository into a browsable https URL.
/// </summary>
internal static class RepositoryUrlNormalizer
{
    private static readonly Dictionary<string, string> ShorthandHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["github"] = "github.com",
        ["gitlab"] = "gitlab.com",
        ["bitbucket"] = "bitbucket.org"
    };

    /// <summary>
    /// Handles <c>git+https://</c>, <c>git+ssh://git@host/</c>, <c>git://</c>, <c>git@host:owner/repo</c>,
    /// <c>github:owner/repo</c> and <c>owner/repo</c>, and removes any <c>.git</c> suffix, trailing slash or fragment.
    /// Returns the input trimmed if it is in a form that is not recognized.
    /// </summary>
    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string value = url.Trim();
        if (value.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
        {
            value = value["git+".Length..];
        }

        int fragmentIndex = value.IndexOf('#');
        if (fragmentIndex >= 0)
        {
            value = value[..fragmentIndex];
        }

        return TrimGitSuffix(ExpandToHttps(value));
    }

    private static string ExpandToHttps(string value)
    {
        // github:owner/repo
        int colonIndex = value.IndexOf(':');
        if (colonIndex > 0 && !value.Contains("://") && ShorthandHosts.TryGetValue(value[..colonIndex], out string? host))
        {
            return $"https://{host}/{value[(colonIndex + 1)..]}";
        }

        // owner/repo means GitHub
        if (colonIndex < 0 && value.Count(c => c == '/') == 1 && !value.StartsWith('/') && !value.EndsWith('/'))
        {
            return $"https://github.com/{value}";
        }

        // git@host:owner/repo
        int atIndex = value.IndexOf('@');
        if (!value.Contains("://") && atIndex >= 0 && colonIndex > atIndex)
        {
            return $"https://{value[(atIndex + 1)..colonIndex]}/{value[(colonIndex + 1)..].TrimStart('/')}";
        }

        // ssh://git@host[:port]/path and git://host/path
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme is "ssh" or "git" or "git+ssh" && !string.IsNullOrEmpty(uri.Host))
        {
            return $"https://{uri.Host}{uri.AbsolutePath}";
        }

        return value;
    }

    private static string TrimGitSuffix(string value)
    {
        value = value.TrimEnd('/');

        return value.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? value[..^".git".Length] : value;
    }
}
