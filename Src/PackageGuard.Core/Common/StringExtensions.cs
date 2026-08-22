using System.Text.RegularExpressions;

namespace PackageGuard.Core.Common;

internal static class StringExtensions
{
    /// <summary>
    /// Determines whether the specified text is <see langword="null"/>, empty, or consists only of white-space characters.
    /// </summary>
    public static bool IsNullOrWhiteSpace(this string? text)
    {
        return string.IsNullOrWhiteSpace(text);
    }

    /// <summary>
    /// Determines whether the specified text is neither <see langword="null"/>, empty, nor composed only of white-space characters.
    /// </summary>
    public static bool IsNotNullOrWhiteSpace(this string? text)
    {
        return !text.IsNullOrWhiteSpace();
    }

    /// <summary>
    /// Determines whether the specified text matches the given wildcard pattern.
    /// </summary>
    /// <param name="text">The text to be evaluated.</param>
    /// <param name="wildcardPattern">The wildcard pattern to match, allowing '?' for single character matches and '*' for multi-character matches.</param>
    public static bool MatchesWildcard(this string text, string wildcardPattern)
    {
        if (text.Equals(wildcardPattern))
        {
            return true;
        }

        string regexPattern = Regex.Escape(wildcardPattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");

        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Determines whether the specified text matches any of the given wildcard patterns.
    /// </summary>
    public static bool MatchesAnyWildcard(this string text, IEnumerable<string> wildcardPatterns)
    {
        return wildcardPatterns.Any(pattern => text.MatchesWildcard(pattern));
    }
}
