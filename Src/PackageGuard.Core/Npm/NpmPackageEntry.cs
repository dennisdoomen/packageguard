using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Represents a single package entry in the npm package-lock.json file.
/// </summary>
[UsedImplicitly]
internal class NpmPackageEntry
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("resolved")]
    public string? Resolved { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }
}
