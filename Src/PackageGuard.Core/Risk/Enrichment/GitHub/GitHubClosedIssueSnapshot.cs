using JetBrains.Annotations;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Lightweight snapshot of a closed GitHub issue.</summary>
internal sealed class GitHubClosedIssueSnapshot
{
    /// <summary>The GitHub issue number.</summary>
    [UsedImplicitly]
    public int Number { get; init; }

    /// <summary>The date and time the issue was closed.</summary>
    public DateTimeOffset? ClosedAt { get; init; }
}
