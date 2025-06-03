using JetBrains.Annotations;

namespace PackageGuard;

public class GlobalSettings
{
    [UsedImplicitly]
    public AllowPolicyItem Allow { get; set; } = new();

    [UsedImplicitly]
    public PolicyItem Deny { get; set; } = new();

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
    }
}
