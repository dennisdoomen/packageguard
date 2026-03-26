using System.Globalization;
using System.Text;
using System.Text.Json;
namespace PackageGuard.Core;

/// <summary>
/// Queries the OSV API for vulnerability data and enriches a <see cref="PackageInfo"/> with the results.
/// </summary>
internal sealed class OsvRiskEnricher : IEnrichPackageRisk
{
    /// <summary>
    /// Shared HTTP client used for all OSV API requests.
    /// </summary>
    private static readonly HttpClient HttpClient = new();

    /// <summary>
    /// OSV result cache keyed by "source|name|version" to avoid redundant API calls.
    /// </summary>
    private static readonly Dictionary<string, OsvPackageRiskResult> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lock that guards thread-safe access to <see cref="Cache"/>.
    /// </summary>
    private static readonly Lock CacheLock = new();

    /// <summary>
    /// Returns <see langword="true"/> if OSV risk data has already been populated for <paramref name="package"/>.
    /// </summary>
    public bool HasCachedData(PackageInfo package) => package.HasOsvRiskData;

    /// <summary>
    /// Queries the OSV API for vulnerabilities affecting <paramref name="package"/> and applies the results to it.
    /// </summary>
    public async Task EnrichAsync(PackageInfo package)
    {
        string cacheKey = $"{package.Source}|{package.Name}|{package.Version}";

        lock (CacheLock)
        {
            if (Cache.TryGetValue(cacheKey, out OsvPackageRiskResult? cached))
            {
                Apply(package, cached);
                return;
            }
        }

        OsvPackageRiskResult result = await QueryAsync(package);

        lock (CacheLock)
        {
            Cache[cacheKey] = result;
        }

        Apply(package, result);
    }

    /// <summary>
    /// Pages through OSV API results for <paramref name="package"/>, aggregating severity and fix data into an <see cref="OsvPackageRiskResult"/>.
    /// </summary>
    private async Task<OsvPackageRiskResult> QueryAsync(PackageInfo package)
    {
        string ecosystem = package.Source switch
        {
            "npm" => "npm",
            _ => "NuGet"
        };

        string? pageToken = null;
        int vulnerabilityCount = 0;
        double maxSeverity = 0;
        bool hasPatchedRecent = false;
        bool hasAvailableFix = false;
        List<double> fixDays = [];

        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.osv.dev/v1/query");
            request.Content = new StringContent(CreateRequestBody(package, ecosystem, pageToken), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("vulns", out JsonElement vulnerabilities) &&
                vulnerabilities.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement vulnerability in vulnerabilities.EnumerateArray())
                {
                    vulnerabilityCount++;
                    maxSeverity = Math.Max(maxSeverity, ReadSeverity(vulnerability));

                    if (HasFix(vulnerability) && IsRecentlyModified(vulnerability))
                    {
                        hasPatchedRecent = true;
                    }

                    if (HasFix(vulnerability))
                    {
                        hasAvailableFix = true;
                    }

                    double? daysToFix = TryGetDaysToFix(vulnerability);
                    if (daysToFix != null)
                    {
                        fixDays.Add(daysToFix.Value);
                    }
                }
            }

            pageToken = root.TryGetProperty("next_page_token", out JsonElement pageTokenElement)
                ? pageTokenElement.GetString()
                : null;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return new OsvPackageRiskResult
        {
            VulnerabilityCount = vulnerabilityCount,
            MaxSeverity = maxSeverity,
            HasPatchedVulnerabilityInLast90Days = hasPatchedRecent,
            HasAvailableSecurityFix = hasAvailableFix,
            MedianVulnerabilityFixDays = ComputeMedian(fixDays)
        };
    }

    /// <summary>
    /// Builds the JSON request body for the OSV query API, optionally including a pagination token.
    /// </summary>
    private static string CreateRequestBody(PackageInfo package, string ecosystem, string? pageToken)
    {
        if (string.IsNullOrWhiteSpace(pageToken))
        {
            return $$"""
                     {"package":{"name":"{{Escape(package.Name)}}","ecosystem":"{{ecosystem}}"},"version":"{{Escape(package.Version)}}"}
                     """;
        }

        return $$"""
                 {"package":{"name":"{{Escape(package.Name)}}","ecosystem":"{{ecosystem}}"},"version":"{{Escape(package.Version)}}","page_token":"{{Escape(pageToken)}}"}
                 """;
    }

    /// <summary>
    /// JSON-escapes backslash and double-quote characters in <paramref name="value"/>.
    /// </summary>
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// Copies all OSV result fields from <paramref name="result"/> onto <paramref name="package"/>.
    /// </summary>
    private static void Apply(PackageInfo package, OsvPackageRiskResult result)
    {
        package.VulnerabilityCount = result.VulnerabilityCount;
        package.MaxVulnerabilitySeverity = result.MaxSeverity;
        package.HasPatchedVulnerabilityInLast90Days = result.HasPatchedVulnerabilityInLast90Days;
        package.HasAvailableSecurityFix = result.HasAvailableSecurityFix;
        package.MedianVulnerabilityFixDays = result.MedianVulnerabilityFixDays;
        package.HasOsvRiskData = true;
    }

    /// <summary>
    /// Returns <see langword="true"/> if any range event within <paramref name="vulnerability"/> contains a "fixed" version.
    /// </summary>
    private static bool HasFix(JsonElement vulnerability)
    {
        if (!vulnerability.TryGetProperty("affected", out JsonElement affected) || affected.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement affectedPackage in affected.EnumerateArray())
        {
            if (!affectedPackage.TryGetProperty("ranges", out JsonElement ranges) || ranges.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement range in ranges.EnumerateArray())
            {
                if (!range.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                if (events.EnumerateArray().Any(e => e.TryGetProperty("fixed", out JsonElement fixedElement) &&
                                                     !string.IsNullOrWhiteSpace(fixedElement.GetString())))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="vulnerability"/> was modified within the last 90 days.
    /// </summary>
    private static bool IsRecentlyModified(JsonElement vulnerability)
    {
        if (vulnerability.TryGetProperty("modified", out JsonElement modifiedElement) &&
            DateTimeOffset.TryParse(modifiedElement.GetString(), out DateTimeOffset modifiedAt))
        {
            return modifiedAt >= DateTimeOffset.UtcNow.AddDays(-90);
        }

        return false;
    }

    /// <summary>
    /// Extracts and returns the highest numeric CVSS or text-mapped severity score from <paramref name="vulnerability"/>.
    /// </summary>
    private static double ReadSeverity(JsonElement vulnerability)
    {
        foreach (JsonElement severity in EnumerateSeverityElements(vulnerability))
        {
            if (severity.TryGetProperty("score", out JsonElement scoreElement))
            {
                double score = ParseScore(scoreElement.GetString());
                if (score > 0)
                {
                    return score;
                }
            }
        }

        foreach (string textSeverity in EnumerateTextSeverities(vulnerability))
        {
            double mappedSeverity = textSeverity.ToUpperInvariant() switch
            {
                "CRITICAL" => 9.5,
                "HIGH" => 8.0,
                "MODERATE" => 6.0,
                "MEDIUM" => 6.0,
                "LOW" => 3.0,
                _ => 0
            };

            if (mappedSeverity > 0)
            {
                return mappedSeverity;
            }
        }

        return 0;
    }

    /// <summary>
    /// Yields all severity JSON elements from the top-level severity array and from each affected-package entry.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateSeverityElements(JsonElement vulnerability)
    {
        if (vulnerability.TryGetProperty("severity", out JsonElement severity) && severity.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in severity.EnumerateArray())
            {
                yield return item;
            }
        }

        if (vulnerability.TryGetProperty("affected", out JsonElement affected) && affected.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement affectedPackage in affected.EnumerateArray())
            {
                if (affectedPackage.TryGetProperty("severity", out JsonElement affectedSeverity) &&
                    affectedSeverity.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in affectedSeverity.EnumerateArray())
                    {
                        yield return item;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Yields text severity strings sourced from <c>ecosystem_specific</c> and <c>database_specific</c> objects within the affected entries.
    /// </summary>
    private static IEnumerable<string> EnumerateTextSeverities(JsonElement vulnerability)
    {
        if (!vulnerability.TryGetProperty("affected", out JsonElement affected) || affected.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement affectedPackage in affected.EnumerateArray())
        {
            if (affectedPackage.TryGetProperty("ecosystem_specific", out JsonElement ecosystemSpecific) &&
                ecosystemSpecific.ValueKind == JsonValueKind.Object &&
                ecosystemSpecific.TryGetProperty("severity", out JsonElement severity))
            {
                string? value = severity.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }

            if (affectedPackage.TryGetProperty("database_specific", out JsonElement databaseSpecific) &&
                databaseSpecific.ValueKind == JsonValueKind.Object &&
                databaseSpecific.TryGetProperty("severity", out JsonElement databaseSeverity))
            {
                string? value = databaseSeverity.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    /// <summary>
    /// Parses a CVSS vector string or plain numeric score into a <see cref="double"/>; returns <c>0</c> when parsing fails.
    /// </summary>
    private static double ParseScore(string? score)
    {
        if (string.IsNullOrWhiteSpace(score))
        {
            return 0;
        }

        if (double.TryParse(score, NumberStyles.Number, CultureInfo.InvariantCulture, out double numericScore))
        {
            return numericScore;
        }

        string[] parts = score.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Select(part => double.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out double value) ? value : 0).FirstOrDefault(value => value > 0);
    }

    /// <summary>
    /// Returns the number of days between the published and modified dates of <paramref name="vulnerability"/> if a fix exists; otherwise <see langword="null"/>.
    /// </summary>
    private static double? TryGetDaysToFix(JsonElement vulnerability)
    {
        if (!HasFix(vulnerability))
        {
            return null;
        }

        DateTimeOffset? publishedAt = TryReadDate(vulnerability, "published");
        DateTimeOffset? modifiedAt = TryReadDate(vulnerability, "modified");
        if (publishedAt is null || modifiedAt is null || modifiedAt < publishedAt)
        {
            return null;
        }

        return (modifiedAt.Value - publishedAt.Value).TotalDays;
    }

    /// <summary>
    /// Reads and parses the date-valued property identified by <paramref name="propertyName"/> from <paramref name="element"/>;
    /// returns <see langword="null"/> if the property is absent or unparseable.
    /// </summary>
    private static DateTimeOffset? TryReadDate(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               DateTimeOffset.TryParse(property.GetString(), out DateTimeOffset value)
            ? value
            : null;
    }

    /// <summary>
    /// Computes the median of <paramref name="values"/>, or returns <see langword="null"/> if the list is empty.
    /// </summary>
    private static double? ComputeMedian(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2.0
            : values[middle];
    }

    /// <summary>
    /// Holds the aggregated OSV vulnerability results for a single package version.
    /// </summary>
    private sealed class OsvPackageRiskResult
    {
        /// <summary>
        /// Total number of vulnerabilities found for the package.
        /// </summary>
        public int VulnerabilityCount { get; init; }

        /// <summary>
        /// Highest severity score across all vulnerabilities found for the package.
        /// </summary>
        public double MaxSeverity { get; init; }

        /// <summary>
        /// Whether at least one vulnerability was patched within the last 90 days.
        /// </summary>
        public bool HasPatchedVulnerabilityInLast90Days { get; init; }

        /// <summary>
        /// Whether at least one vulnerability has an available security fix.
        /// </summary>
        public bool HasAvailableSecurityFix { get; init; }

        /// <summary>
        /// Median number of days from vulnerability publication to fix, or <see langword="null"/> if no fix data is available.
        /// </summary>
        public double? MedianVulnerabilityFixDays { get; init; }
    }
}
