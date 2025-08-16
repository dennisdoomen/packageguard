using JetBrains.Annotations;

namespace PackageGuard;

public class PolicySettings
{
    [UsedImplicitly]
    public AllowPolicyItem Allow { get; set; } = new();

    [UsedImplicitly]
    public DenyPolicyItem Deny { get; set; } = new();

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
    }

    /// <summary>
    /// Merges two <see cref="PolicySettings"/> objects, with the second overriding the first where specified.
    /// For collections, items are accumulated rather than replaced.
    /// </summary>
    public void MergeWith(PolicySettings source)
    {
        // Merge Allow settings
        if (source.Allow.Packages.Length > 0)
        {
            Allow.Packages = Allow.Packages.Concat(source.Allow.Packages).ToArray();
        }

        if (source.Allow.Licenses.Length > 0)
        {
            Allow.Licenses = Allow.Licenses.Concat(source.Allow.Licenses).ToArray();
        }

        if (source.Allow.Feeds.Length > 0)
        {
            Allow.Feeds = Allow.Feeds.Concat(source.Allow.Feeds).ToArray();
        }

        // For boolean settings, later values override earlier ones
        // We need to track if the source actually specified a value different from default
        Allow.Prerelease = source.Allow.Prerelease;

        // Merge Deny settings
        if (source.Deny.Packages.Length > 0)
        {
            Deny.Packages = Deny.Packages.Concat(source.Deny.Packages).ToArray();
        }

        if (source.Deny.Licenses.Length > 0)
        {
            Deny.Licenses = Deny.Licenses.Concat(source.Deny.Licenses).ToArray();
        }

        // For boolean settings, later values override earlier ones
        Deny.Prerelease = source.Deny.Prerelease;

        // Merge IgnoredFeeds
        if (source.IgnoredFeeds.Length > 0)
        {
            IgnoredFeeds = IgnoredFeeds.Concat(source.IgnoredFeeds).ToArray();
        }
    }
}
