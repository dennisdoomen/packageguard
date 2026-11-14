namespace PackageGuard.Reports;

/// <summary>
/// An evidence label/value pair, optionally accompanied by the full list of items (e.g. package names) behind
/// the value, rendered as a collapsed-by-default panel, and a URL that the value links to.
/// </summary>
internal readonly record struct EvidenceEntry(
    string Label,
    string Value,
    IReadOnlyCollection<string>? Items = null,
    string? Url = null)
{
    /// <summary>
    /// Creates an entry from a label and a value, without items or a URL.
    /// </summary>
    public static implicit operator EvidenceEntry((string Label, string Value) pair) => new(pair.Label, pair.Value);
}
