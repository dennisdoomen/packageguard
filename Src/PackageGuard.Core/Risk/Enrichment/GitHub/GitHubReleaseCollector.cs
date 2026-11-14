using System.Text.Json;
using NuGet.Versioning;
using PackageGuard.Core.GitHub;
using static PackageGuard.Core.Common.JsonElementReader;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>Collects the release cadence, versioning and signing metrics of a repository's GitHub releases.</summary>
internal sealed class GitHubReleaseCollector
{
    private readonly GitHubApiClient apiClient;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubReleaseCollector(GitHubApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    /// <summary>Fetches releases and computes semver/interval/correction metrics.</summary>
    public async Task<GitHubReleaseData> CollectAsync(GitHubRepositoryContext repository)
    {
        try
        {
            using JsonDocument? releaseDoc = await apiClient.GetJsonAsync($"{repository.ApiRoot}/releases?per_page=10",
                fields: GitHubResponseFields.Of("tag_name", "body", "published_at", "created_at", "prerelease",
                    "target_commitish", "immutable"));
            if (releaseDoc?.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new GitHubReleaseData();
            }

            return Analyze(releaseDoc.RootElement.EnumerateArray().ToArray());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return new GitHubReleaseData();
        }
    }

    /// <summary>Computes the fraction of consecutive release transitions that were major-version bumps.</summary>
    internal static double? ComputeMajorReleaseRatio(
        IReadOnlyList<(DateTimeOffset PublishedAt, NuGetVersion Version)> releases)
    {
        if (releases.Count < 2)
        {
            return null;
        }

        var orderedReleases = releases
            .OrderBy(release => release.PublishedAt)
            .ToArray();

        int transitionCount = orderedReleases.Length - 1;
        int majorTransitionCount = 0;

        for (int i = 1; i < orderedReleases.Length; i++)
        {
            if (orderedReleases[i].Version.Major > orderedReleases[i - 1].Version.Major)
            {
                majorTransitionCount++;
            }
        }

        return transitionCount > 0 ? majorTransitionCount / (double)transitionCount : null;
    }

    /// <summary>Computes the release metrics from the listed releases.</summary>
    private static GitHubReleaseData Analyze(JsonElement[] releases)
    {
        bool hasReleaseNotes = releases.Any(release =>
            release.TryGetProperty("body", out JsonElement bodyElement) &&
            !string.IsNullOrWhiteSpace(bodyElement.GetString()) &&
            bodyElement.GetString()!.Length >= 80);

        DateTimeOffset? lastReleaseAt = releases
            .Select(ReadPublishedAt)
            .Where(date => date.HasValue)
            .OrderByDescending(date => date)
            .FirstOrDefault();

        int prereleaseCount = releases.Count(release =>
            release.TryGetProperty("prerelease", out JsonElement prereleaseElement) &&
            prereleaseElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            prereleaseElement.GetBoolean());

        List<DateTimeOffset> publishedDates = releases
            .Select(ReadPublishedAt)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .OrderBy(date => date)
            .ToList();

        (int semVerReleaseCount, List<(DateTimeOffset PublishedAt, NuGetVersion Version)> parsedReleaseVersions) =
            ParseReleaseVersions(releases);

        return new GitHubReleaseData
        {
            LastReleaseAt = lastReleaseAt,
            HasReleaseNotes = hasReleaseNotes,
            HasSemVerReleaseTags = releases.Length > 0 ? semVerReleaseCount == releases.Length : null,
            MeanReleaseIntervalDays = ComputeAverageIntervalDays(publishedDates),
            MajorReleaseRatio = ComputeMajorReleaseRatio(parsedReleaseVersions),
            PrereleaseRatio = releases.Length > 0 ? prereleaseCount / (double)releases.Length : null,
            RapidReleaseCorrectionCount = CountRapidCorrections(publishedDates),
            HasVerifiedReleaseSignature = releases.Select(TryReadVerifiedReleaseSignature).FirstOrDefault()
        };
    }

    /// <summary>Reads when a release was published, falling back to when it was created.</summary>
    private static DateTimeOffset? ReadPublishedAt(JsonElement release) =>
        TryReadDate(release, "published_at") ?? TryReadDate(release, "created_at");

    /// <summary>
    /// Counts the releases whose tag is a SemVer version, and returns the parsed versions of those that have a date.
    /// </summary>
    private static (int SemVerReleaseCount, List<(DateTimeOffset PublishedAt, NuGetVersion Version)> Versions)
        ParseReleaseVersions(JsonElement[] releases)
    {
        int semVerReleaseCount = 0;
        List<(DateTimeOffset PublishedAt, NuGetVersion Version)> parsedReleaseVersions = [];

        foreach (JsonElement release in releases)
        {
            string? tagName = release.TryGetProperty("tag_name", out JsonElement tagNameElement)
                ? tagNameElement.GetString()
                : null;

            if (!TryParseReleaseVersion(tagName, out NuGetVersion? parsedVersion) || parsedVersion is null)
            {
                continue;
            }

            semVerReleaseCount++;
            DateTimeOffset? publishedAt = ReadPublishedAt(release);
            if (publishedAt != null)
            {
                parsedReleaseVersions.Add((publishedAt.Value, parsedVersion));
            }
        }

        return (semVerReleaseCount, parsedReleaseVersions);
    }

    /// <summary>Counts the releases that followed the previous one within three days.</summary>
    private static int CountRapidCorrections(List<DateTimeOffset> publishedDates)
    {
        int rapidCorrections = 0;
        for (int i = 1; i < publishedDates.Count; i++)
        {
            if ((publishedDates[i] - publishedDates[i - 1]).TotalDays <= 3)
            {
                rapidCorrections++;
            }
        }

        return rapidCorrections;
    }

    /// <summary>Tries to read whether a GitHub release has a verified signature.</summary>
    private static bool? TryReadVerifiedReleaseSignature(JsonElement release)
    {
        if (!release.TryGetProperty("target_commitish", out _))
        {
            return null;
        }

        if (release.TryGetProperty("immutable", out JsonElement immutableElement) &&
            immutableElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return immutableElement.GetBoolean();
        }

        return null;
    }

    /// <summary>Strips v/release prefixes and tries to parse the tag as a NuGetVersion.</summary>
    private static bool TryParseReleaseVersion(string? tagName, out NuGetVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        string normalized = tagName.Trim();
        if (normalized.StartsWith("release-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["release-".Length..];
        }

        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        return NuGetVersion.TryParse(normalized, out version);
    }

    /// <summary>Computes the mean interval in days between consecutive dates.</summary>
    private static double? ComputeAverageIntervalDays(IReadOnlyList<DateTimeOffset> dates)
    {
        if (dates.Count < 2)
        {
            return null;
        }

        double total = 0;
        for (int i = 1; i < dates.Count; i++)
        {
            total += (dates[i] - dates[i - 1]).TotalDays;
        }

        return total / (dates.Count - 1);
    }
}
