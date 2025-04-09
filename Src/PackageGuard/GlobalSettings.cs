using JetBrains.Annotations;

namespace PackageGuard;

public class GlobalSettings
{
    [UsedImplicitly]
    public PolicyItem Allow { get; set; } = new();

    [UsedImplicitly]
    public PolicyItem Deny { get; set; } = new();

    public class PolicyItem
    {
        [UsedImplicitly]
        public string[] Packages { get; set; } = [];

        [UsedImplicitly]
        public string[] Licenses { get; set; } = [];
    }
}
