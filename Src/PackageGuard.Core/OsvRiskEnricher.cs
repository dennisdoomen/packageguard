using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PackageGuard.Core;

internal sealed class OsvRiskEnricher : IEnrichPackageRisk
{
    private static readonly HttpClient HttpClient = new();
    private static readonly Dictionary<string, OsvPackageRiskResult> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock CacheLock = new();

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

    private async Task<OsvPackageRiskResult> QueryAsync(PackageInfo package)
    {
        string? ecosystem = package.Source switch
        {
            "npm" => "npm",
            _ => "NuGet"
        };

        if (ecosystem is null)
        {
            return new OsvPackageRiskResult();
        }

        string? pageToken = null;
        int vulnerabilityCount = 0;
        double maxSeverity = 0;
        bool hasPatchedRecent = false;

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
            HasPatchedVulnerabilityInLast90Days = hasPatchedRecent
        };
    }

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

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void Apply(PackageInfo package, OsvPackageRiskResult result)
    {
        package.VulnerabilityCount = result.VulnerabilityCount;
        package.MaxVulnerabilitySeverity = result.MaxSeverity;
        package.HasPatchedVulnerabilityInLast90Days = result.HasPatchedVulnerabilityInLast90Days;
    }

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

    private static bool IsRecentlyModified(JsonElement vulnerability)
    {
        if (vulnerability.TryGetProperty("modified", out JsonElement modifiedElement) &&
            DateTimeOffset.TryParse(modifiedElement.GetString(), out DateTimeOffset modifiedAt))
        {
            return modifiedAt >= DateTimeOffset.UtcNow.AddDays(-90);
        }

        return false;
    }

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

    private static double ParseScore(string? score)
    {
        if (string.IsNullOrWhiteSpace(score))
        {
            return 0;
        }

        if (double.TryParse(score, out double numericScore))
        {
            return numericScore;
        }

        string[] parts = score.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Select(part => double.TryParse(part, out double value) ? value : 0).FirstOrDefault(value => value > 0);
    }

    private sealed class OsvPackageRiskResult
    {
        public int VulnerabilityCount { get; init; }

        public double MaxSeverity { get; init; }

        public bool HasPatchedVulnerabilityInLast90Days { get; init; }
    }
}
