namespace PackageGuard.Core.GitHub;

/// <summary>
/// Describes how valuable a GitHub API request is, which decides whether it still gets made once the rate limit
/// budget runs low.
/// </summary>
internal enum GitHubRequestImportance
{
    /// <summary>
    /// The request carries a signal that the rest of the analysis depends on and is attempted for as long as the
    /// rate limit allows any request at all.
    /// </summary>
    Essential,

    /// <summary>
    /// The request refines a risk signal but is not worth spending the last of the rate limit budget on. Requests of
    /// this kind are skipped once the remaining budget drops into the reserve.
    /// </summary>
    Optional
}
