using JetBrains.Annotations;

namespace PackageGuard;

public class GlobalSettings
{
    [UsedImplicitly]
    public PolicyItem AllowList { get; set; } = new();

    [UsedImplicitly]
    public PolicyItem DenyList { get; set; } = new();

    public class PolicyItem
    {
        [UsedImplicitly]
        public string[] Packages { get; set; } = [];

        [UsedImplicitly]
        public string[] Licenses { get; set; } = [];
    }
}
