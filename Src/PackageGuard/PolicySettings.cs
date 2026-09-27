using JetBrains.Annotations;

namespace PackageGuard;

public class PolicySettings
{
    [UsedImplicitly]
    public AllowPolicyItem Allow { get; set; } = new();

    [UsedImplicitly]
    public DenyPolicyItem Deny { get; set; } = new();

    [UsedImplicitly]
    public WarnPolicyItem Warn { get; set; } = new();

    [UsedImplicitly]
    public RiskExceptionItem[] RiskExceptions { get; set; } = [];

    [UsedImplicitly]
    public string[] IgnoredFeeds { get; set; } = [];

    public class PolicyItem
    {
        [UsedImplicitly]
        public string[] Packages { get; set; } = [];

        [UsedImplicitly]
        public string[] Licenses { get; set; } = [];
    }

    public class AllowPolicyItem : PolicyItem
    {
        [UsedImplicitly]
        public string[] Feeds { get; set; } = [];

        [UsedImplicitly]
        public bool Prerelease { get; set; } = true;
    }

    public class DenyPolicyItem : PolicyItem
    {
        [UsedImplicitly]
        public bool Prerelease { get; set; }

        [UsedImplicitly]
        public double? MaxOverallRisk { get; set; }

        [UsedImplicitly]
        public double? MaxLegalRisk { get; set; }

        [UsedImplicitly]
        public double? MaxSecurityRisk { get; set; }

        [UsedImplicitly]
        public double? MaxOperationalRisk { get; set; }

        [UsedImplicitly]
        public double? MaxOsvSeverityScore { get; set; }

        [UsedImplicitly]
        public bool DenyUnsigned { get; set; }

        [UsedImplicitly]
        public bool DenyDeprecated { get; set; }

        [UsedImplicitly]
        public bool DenyWithoutRepository { get; set; }

        [UsedImplicitly]
        public Dictionary<string, int> MinPackageAgeDays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class WarnPolicyItem : PolicyItem
    {
        [UsedImplicitly]
        public bool Prerelease { get; set; }
    }

    public class RiskExceptionItem
    {
        [UsedImplicitly]
        public string Package { get; set; } = "";

        [UsedImplicitly]
        public string? Versions { get; set; }

        [UsedImplicitly]
        public string Reason { get; set; } = "";

        [UsedImplicitly]
        public DateOnly? ExpiresOn { get; set; }
    }
}
