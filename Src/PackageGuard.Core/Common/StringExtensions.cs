using System.Text.RegularExpressions;

namespace PackageGuard.Core.Common;

internal static class StringExtensions
{
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
}
