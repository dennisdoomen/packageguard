namespace PackageGuard.Core.Policy;

/// <summary>
/// Defines whether a policy violation fails the build or is reported as a warning.
/// </summary>
public enum PolicySeverity
{
    /// <summary>
    /// The violation fails the build.
    /// </summary>
    Error,

    /// <summary>
    /// The violation is reported but does not fail the build.
    /// </summary>
    Warning
}
