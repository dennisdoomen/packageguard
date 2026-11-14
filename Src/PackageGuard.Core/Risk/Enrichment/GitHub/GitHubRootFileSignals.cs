namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>
/// The pending collections of the signals that depend on the listing of the repository root, started once that
/// listing is in.
/// </summary>
internal sealed class GitHubRootFileSignals
{
    /// <summary>Whether the repository has a changelog and whether it is boilerplate.</summary>
    public required Task<Tracked<GitHubChangelogData>> Changelog { get; init; }

    /// <summary>Whether the repository has a security policy and how detailed it is.</summary>
    public required Task<Tracked<GitHubSecurityPolicyData>> SecurityPolicy { get; init; }

    /// <summary>What the workflow files reveal about testing, coverage, provenance and dependency automation.</summary>
    public required Task<Tracked<GitHubWorkflowFileSignals>> WorkflowFiles { get; init; }

    /// <summary>When the README last changed.</summary>
    public required Task<DateTimeOffset?> ReadmeUpdatedAt { get; init; }

    /// <summary>When the changelog last changed.</summary>
    public required Task<DateTimeOffset?> ChangelogUpdatedAt { get; init; }

    /// <summary>Completes when every collection has finished.</summary>
    public Task WhenAllAsync() =>
        Task.WhenAll(Changelog, SecurityPolicy, WorkflowFiles, ReadmeUpdatedAt, ChangelogUpdatedAt);
}
