namespace PackageGuard.Core;

public record PolicyViolation(string PackageId, string Version, string License, string[] Projects);
