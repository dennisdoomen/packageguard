namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Classifies the GitHub accounts and author associations that show up in commits, issues and pull requests.</summary>
internal static class GitHubIdentity
{
    /// <summary>Returns true if the login/email looks like a bot (e.g. [bot] suffix, dependabot).</summary>
    public static bool IsBot(string? identity) =>
        string.IsNullOrWhiteSpace(identity) ||
        identity.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) ||
        identity.Contains("dependabot", StringComparison.OrdinalIgnoreCase) ||
        identity.Contains("copilot", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true if the author association indicates an external (non-member) contributor.</summary>
    public static bool IsExternalAuthorAssociation(string? association) =>
        association is "NONE" or "FIRST_TIME_CONTRIBUTOR" or "FIRST_TIMER" or "CONTRIBUTOR";

    /// <summary>Returns true if the author association is OWNER, MEMBER, or COLLABORATOR.</summary>
    public static bool IsMaintainerAssociation(string? association) =>
        association is "OWNER" or "MEMBER" or "COLLABORATOR";
}
