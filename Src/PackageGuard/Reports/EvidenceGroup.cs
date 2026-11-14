namespace PackageGuard.Reports;

/// <summary>
/// A titled group of evidence about a package, rendered as its own card in the detail section of the report.
/// </summary>
internal sealed record EvidenceGroup(string Title, IReadOnlyCollection<EvidenceEntry> Entries);
