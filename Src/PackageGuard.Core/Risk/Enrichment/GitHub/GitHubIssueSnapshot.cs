using JetBrains.Annotations;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Lightweight snapshot of an open GitHub issue for processing.</summary>
internal sealed class GitHubIssueSnapshot
{
    /// <summary>The GitHub issue number.</summary>
    [UsedImplicitly]
    public int Number { get; init; }

    /// <summary>The date and time the issue was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Indicates whether the issue has a label indicating critical severity or a security concern.</summary>
    public bool IsCritical { get; init; }

    /// <summary>The URL to fetch comments for this issue.</summary>
    public string? CommentsUrl { get; init; }
}
