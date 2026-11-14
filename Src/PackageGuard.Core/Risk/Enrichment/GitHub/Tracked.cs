namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>A value fetched from GitHub, together with whether all requests behind it got an answer.</summary>
/// <param name="Value">The fetched value, which falls back to its defaults for the requests that failed.</param>
/// <param name="IsComplete">Indicates whether every request behind the value got an answer.</param>
internal sealed record Tracked<T>(T Value, bool IsComplete)
{
    /// <summary>Returns the value, or <see langword="null"/> when a request behind it got no answer.</summary>
    public T? KnownValue => IsComplete ? Value : default;
}
