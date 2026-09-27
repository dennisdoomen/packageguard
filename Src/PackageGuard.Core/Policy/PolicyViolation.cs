namespace PackageGuard.Core.Policy;

public record PolicyViolation(
    string PackageId,
    string Version,
    string License,
    string[] Projects,
    string FeedName,
    string FeedUrl,
    string Reason = "",
    bool IsWarning = false);
