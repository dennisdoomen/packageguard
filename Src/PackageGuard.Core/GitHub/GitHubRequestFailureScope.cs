namespace PackageGuard.Core.GitHub;

/// <summary>
/// Records whether any GitHub request made within the scope came back without an answer, because the rate limit
/// budget was spent or GitHub kept failing. A resource that does not exist is an answer and does not count as a
/// failure, and neither does an optional request that was skipped by design to save the budget.
/// </summary>
/// <remarks>
/// Scopes flow with the asynchronous call that opened them and nest: a failure marks the innermost scope and every
/// scope around it. This lets a caller tell "the file is not there" apart from "we could not find out".
/// </remarks>
internal sealed class GitHubRequestFailureScope : IDisposable
{
    private static readonly AsyncLocal<GitHubRequestFailureScope?> CurrentScope = new();

    private readonly GitHubRequestFailureScope? parent;
    private volatile bool hasFailures;

    private GitHubRequestFailureScope(GitHubRequestFailureScope? parent)
    {
        this.parent = parent;
    }

    /// <summary>
    /// Indicates whether at least one request within the scope failed to get an answer.
    /// </summary>
    public bool HasFailures => hasFailures;

    /// <summary>
    /// Opens a scope that collects the failures of the requests made from the current asynchronous flow.
    /// </summary>
    public static GitHubRequestFailureScope Begin()
    {
        var scope = new GitHubRequestFailureScope(CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>
    /// Records a failed request in the scopes that are open for the current asynchronous flow.
    /// </summary>
    public static void ReportFailure()
    {
        for (GitHubRequestFailureScope? scope = CurrentScope.Value; scope is not null; scope = scope.parent)
        {
            scope.hasFailures = true;
        }
    }

    /// <summary>
    /// Closes the scope, so that later requests from the same flow report to the enclosing scope again.
    /// </summary>
    public void Dispose()
    {
        if (CurrentScope.Value == this)
        {
            CurrentScope.Value = parent;
        }
    }
}
