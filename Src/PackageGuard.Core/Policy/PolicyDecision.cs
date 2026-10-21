namespace PackageGuard.Core.Policy;

/// <summary>
/// Describes the outcome of evaluating a package against an <see cref="AllowList"/>, <see cref="DenyList"/> or
/// <see cref="WarnList"/>.
/// </summary>
internal sealed record PolicyDecision
{
    /// <summary>
    /// Indicates that a rule of the list applies to the package. For an <see cref="AllowList"/> this means the package
    /// is allowed; for a <see cref="DenyList"/> or <see cref="WarnList"/> it means the package is denied or flagged.
    /// </summary>
    public required bool IsMatch { get; init; }

    /// <summary>
    /// The human-readable rule that produced this outcome.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// The configuration file that introduced the rule, or <see langword="null"/> if it is unknown.
    /// </summary>
    public string? SourceFile { get; init; }

    /// <summary>
    /// A hint on how to resolve the violation this outcome causes, or an empty string if it causes none or there is no
    /// useful hint.
    /// </summary>
    public string Resolution { get; init; } = "";
}
