using System.Text.RegularExpressions;

namespace PackageGuard.Core.Common;

internal static class StringExtensions
{
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
