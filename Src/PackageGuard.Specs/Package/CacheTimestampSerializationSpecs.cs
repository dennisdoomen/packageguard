using System;
using FluentAssertions;
using MemoryPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Package;

namespace PackageGuard.Specs.Package;

[TestClass]
public class CacheTimestampSerializationSpecs
{
    [TestMethod]
    public void Can_round_trip_package_timestamps_with_offsets_and_nulls()
    {
        DateTimeOffset timestamp = new(2025, 3, 5, 12, 30, 0, TimeSpan.FromHours(-7));
        var package = new PackageInfo
        {
            CacheUpdatedAt = timestamp,
            PublishedAt = timestamp,
            OwnerCreatedAt = timestamp,
            ReadmeUpdatedAt = timestamp,
            ChangelogUpdatedAt = null,
            LatestStablePublishedAt = timestamp
        };

        PackageInfo result = MemoryPackSerializer.Deserialize<PackageInfo>(MemoryPackSerializer.Serialize(package))!;

        result.CacheUpdatedAt.Should().BeExactly(timestamp);
        result.PublishedAt.Should().BeExactly(timestamp);
        result.OwnerCreatedAt.Should().BeExactly(timestamp);
        result.ReadmeUpdatedAt.Should().BeExactly(timestamp);
        result.ChangelogUpdatedAt.Should().BeNull();
        result.LatestStablePublishedAt.Should().BeExactly(timestamp);
    }

    [TestMethod]
    public void Can_round_trip_repository_timestamps_with_offsets_and_nulls()
    {
        DateTimeOffset timestamp = new(2025, 3, 5, 12, 30, 0, TimeSpan.FromHours(-7));
        var entry = new GitHubRepositoryRiskCacheEntry
        {
            RepositoryApiRoot = "https://api.github.com/repos/example/example",
            StoredAt = timestamp,
            Data = new GitHubRepositoryRiskData
            {
                OwnerCreatedAt = timestamp,
                ReadmeUpdatedAt = timestamp,
                ChangelogUpdatedAt = null,
                LastReleaseAt = timestamp
            }
        };

        GitHubRepositoryRiskCacheEntry result =
            MemoryPackSerializer.Deserialize<GitHubRepositoryRiskCacheEntry>(MemoryPackSerializer.Serialize(entry))!;

        result.StoredAt.Should().BeExactly(timestamp);
        result.Data!.OwnerCreatedAt.Should().BeExactly(timestamp);
        result.Data.ReadmeUpdatedAt.Should().BeExactly(timestamp);
        result.Data.ChangelogUpdatedAt.Should().BeNull();
        result.Data.LastReleaseAt.Should().BeExactly(timestamp);
    }

    [TestMethod]
    public void Can_read_and_write_the_legacy_response_cache_layout()
    {
        DateTimeOffset timestamp = new(2025, 3, 5, 12, 30, 0, TimeSpan.FromHours(-7));
        var legacy = new LegacyResponseCacheEntry { Url = "https://example.com", StoredAt = timestamp, Fields = "fields" };

        GitHubResponseCacheEntry updated =
            MemoryPackSerializer.Deserialize<GitHubResponseCacheEntry>(MemoryPackSerializer.Serialize(legacy))!;
        LegacyResponseCacheEntry restored =
            MemoryPackSerializer.Deserialize<LegacyResponseCacheEntry>(MemoryPackSerializer.Serialize(updated))!;

        updated.StoredAt.Should().BeExactly(timestamp);
        restored.StoredAt.Should().BeExactly(timestamp);
        restored.Url.Should().Be(legacy.Url);
        restored.Fields.Should().Be(legacy.Fields);
    }
}

#pragma warning disable MEMPACK043
[MemoryPackable]
internal sealed partial class LegacyResponseCacheEntry
{
    public string Url { get; set; } = string.Empty;
    public string ETag { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsNotFound { get; set; }
    public DateTimeOffset StoredAt { get; set; }
    public string Fields { get; set; }
}
#pragma warning restore MEMPACK043
