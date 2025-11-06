using System.Text.Json.Serialization;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Represents the structure of an npm package-lock.json file.
/// </summary>
internal class NpmPackageLock
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("lockfileVersion")]
    public int LockfileVersion { get; set; }

    [JsonPropertyName("packages")]
    public Dictionary<string, NpmPackageEntry> Packages { get; set; } = new();
}
