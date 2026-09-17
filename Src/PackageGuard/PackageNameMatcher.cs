namespace PackageGuard;

/// <summary>
/// Resolves a user-typed package name against the packages discovered during analysis, tolerating partial
/// and misspelled input so callers of the <c>explain</c> command don't need to type an exact identifier.
/// </summary>
internal static class PackageNameMatcher
{
    /// <summary>
    /// The maximum number of "Did you mean" suggestions returned when nothing matches exactly or partially.
    /// </summary>
    private const int MaxSuggestions = 5;

    /// <summary>
    /// The maximum edit distance, relative to the query length, for a name to be suggested as a near match.
    /// </summary>
    private const double MaxRelativeDistance = 0.5;

    /// <summary>
    /// Resolves <paramref name="query"/> against <paramref name="candidateNames"/>: first by exact
    /// case-insensitive match, then by case-insensitive substring, then by edit-distance suggestions.
    /// </summary>
    public static PackageNameMatch Resolve(string query, IReadOnlyCollection<string> candidateNames)
    {
        string[] distinctNames = candidateNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        string? exactMatch = distinctNames.FirstOrDefault(name => name.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            return PackageNameMatch.Exact(exactMatch);
        }

        string[] substringMatches = distinctNames
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.Length)
            .ToArray();

        if (substringMatches.Length > 0)
        {
            return PackageNameMatch.WithSuggestions(substringMatches.Take(MaxSuggestions).ToArray());
        }

        string[] closeMatches = distinctNames
            .Select(name => (Name: name, Distance: LevenshteinDistance(query, name)))
            .Where(candidate => candidate.Distance <= Math.Max(1, query.Length * MaxRelativeDistance))
            .OrderBy(candidate => candidate.Distance)
            .Take(MaxSuggestions)
            .Select(candidate => candidate.Name)
            .ToArray();

        return PackageNameMatch.WithSuggestions(closeMatches);
    }

    /// <summary>
    /// Computes the classic Levenshtein edit distance between two strings, case-insensitively.
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        int[,] distances = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++)
        {
            distances[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            distances[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;

                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[a.Length, b.Length];
    }
}

/// <summary>
/// The outcome of resolving a package name query: either a single confirmed match, or a set of
/// candidate names to suggest to the user (empty when nothing was close enough to suggest).
/// </summary>
internal sealed class PackageNameMatch
{
    private PackageNameMatch(string? matchedName, IReadOnlyList<string> suggestions)
    {
        MatchedName = matchedName;
        Suggestions = suggestions;
    }

    /// <summary>
    /// Gets the resolved package name, or <see langword="null"/> when no exact match was found.
    /// </summary>
    public string? MatchedName { get; }

    /// <summary>
    /// Gets the candidate names to suggest to the user. Populated only when <see cref="MatchedName"/> is
    /// <see langword="null"/>, or when a single partial match was found (in which case it also becomes
    /// <see cref="MatchedName"/>).
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; }

    public static PackageNameMatch Exact(string name) => new(name, [name]);

    public static PackageNameMatch WithSuggestions(IReadOnlyList<string> suggestions) =>
        new(suggestions.Count == 1 ? suggestions[0] : null, suggestions);
}
