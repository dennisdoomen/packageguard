namespace PackageGuard.Core.Policy;

/// <summary>
/// Describes the outcome of evaluating a package against an <see cref="AllowList"/> or <see cref="DenyList"/>,
/// including the human-readable rule that produced it and the configuration file it came from, if known.
/// </summary>
internal sealed record PolicyDecision(bool IsMatch, string Reason, string? SourceFile = null);
