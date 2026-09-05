using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Risk.Enrichment;

/// <summary>
/// Queries the OSV API for vulnerability data and enriches a <see cref="PackageInfo"/> with the results.
/// </summary>
internal sealed class OsvRiskEnricher(ILogger logger, HttpClient? httpClient = null) : IEnrichPackageRisk, IPrimeRiskData
{
    /// <summary>
    /// Shared HTTP client used for all OSV API requests.
    /// </summary>
    private static readonly HttpClient SharedHttpClient = new();

    /// <summary>
    /// The client this enricher sends its requests through.
    /// </summary>
    private readonly HttpClient httpClient = httpClient ?? SharedHttpClient;

    /// <summary>
    /// OSV result cache keyed by "source|name|version" to avoid redundant API calls.
    /// </summary>
    private static readonly Dictionary<string, OsvPackageRiskResult> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lock that guards thread-safe access to <see cref="Cache"/>.
    /// </summary>
    private static readonly Lock CacheLock = new();

    /// <summary>
    /// The number of package queries sent in one batch request, which is the maximum the OSV API accepts.
    /// </summary>
    private const int MaxQueriesPerBatch = 1000;

    /// <summary>
    /// The number of vulnerability detail requests that are in flight at the same time.
    /// </summary>
    private const int MaxConcurrentDetailRequests = 8;

    /// <summary>
    /// Returns <see langword="true"/> if OSV risk data has already been populated for <paramref name="package"/>.
    /// </summary>
    public bool HasCachedData(PackageInfo package) => package.HasOsvRiskData;

    /// <summary>
    /// Queries the OSV API for vulnerabilities affecting <paramref name="package"/> and applies the results to it.
    /// </summary>
    public async Task EnrichAsync(PackageInfo package)
    {
        string cacheKey = CreateCacheKey(package);

        lock (CacheLock)
        {
            if (Cache.TryGetValue(cacheKey, out OsvPackageRiskResult? cached))
            {
                Apply(package, cached);
                return;
            }
        }

        OsvPackageRiskResult result;
        try
        {
            result = await QueryAsync(package);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to query OSV vulnerability data from {OsvApiUrl} for {Name} {Version}",
                "https://api.osv.dev/v1/query", package.Name, package.Version);
            return;
        }

        lock (CacheLock)
        {
            Cache[cacheKey] = result;
        }

        Apply(package, result);
    }

    /// <summary>
    /// Builds the key a package's OSV result is cached under.
    /// </summary>
    private static string CreateCacheKey(PackageInfo package) =>
        $"{package.Source}|{package.Name}|{package.Version}";

    /// <summary>
    /// Discards the cross-package result cache. Only used by the tests.
    /// </summary>
    internal static void ClearCache()
    {
        lock (CacheLock)
        {
            Cache.Clear();
        }
    }

    /// <summary>
    /// Looks up every package in as few requests as possible, before the per-package enrichment runs.
    /// </summary>
    /// <remarks>
    /// The single-package endpoint costs one request per package. The batch endpoint takes up to a thousand queries
    /// at a time but answers with vulnerability identifiers only, so the details of the few vulnerabilities that
    /// actually turn up are fetched afterwards. A solution with hundreds of packages and a handful of vulnerabilities
    /// goes from hundreds of requests to a couple.
    /// </remarks>
    /// <param name="packages">The packages to look up.</param>
    public async Task PrimeAsync(IReadOnlyCollection<PackageInfo> packages)
    {
        PackageInfo[] pending = packages.Where(NeedsLookup).ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            await PrimeBatchesAsync(pending);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not look up vulnerabilities in bulk; falling back to one request per package");
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the package still needs an OSV lookup this run.
    /// </summary>
    private static bool NeedsLookup(PackageInfo package)
    {
        if (package.HasOsvRiskData)
        {
            return false;
        }

        lock (CacheLock)
        {
            return !Cache.ContainsKey(CreateCacheKey(package));
        }
    }

    /// <summary>
    /// Runs the batch query for every chunk of packages and stores the assembled results in the cache.
    /// </summary>
    private async Task PrimeBatchesAsync(PackageInfo[] pending)
    {
        logger.LogDebug("Looking up vulnerabilities for {Count} packages in bulk", pending.Length);

        foreach (PackageInfo[] batch in pending.Chunk(MaxQueriesPerBatch))
        {
            IReadOnlyList<OsvBatchMatch> matchesPerPackage = await QueryBatchAsync(batch);
            if (matchesPerPackage.Count != batch.Length)
            {
                return;
            }

            await StoreBatchResultsAsync(batch, matchesPerPackage);
        }
    }

    /// <summary>
    /// Sends one batch query and returns what was found for each package, in the same order.
    /// </summary>
    private async Task<IReadOnlyList<OsvBatchMatch>> QueryBatchAsync(PackageInfo[] batch)
    {
        string body = $$"""{"queries":[{{string.Join(",", batch.Select(CreateQuery))}}]}""";
        using JsonDocument doc = await PostAsync("https://api.osv.dev/v1/querybatch", body);

        if (!doc.RootElement.TryGetProperty("results", out JsonElement results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return results.EnumerateArray().Select(ReadBatchMatch).ToArray();
    }

    /// <summary>
    /// Reads the vulnerability identifiers of a single batch result, recording whether the API had to truncate it.
    /// </summary>
    private static OsvBatchMatch ReadBatchMatch(JsonElement result)
    {
        if (ReadPageToken(result) is { Length: > 0 })
        {
            return new OsvBatchMatch([], IsTruncated: true);
        }

        if (!result.TryGetProperty("vulns", out JsonElement vulnerabilities) ||
            vulnerabilities.ValueKind != JsonValueKind.Array)
        {
            return new OsvBatchMatch([], IsTruncated: false);
        }

        string[] identifiers = vulnerabilities.EnumerateArray()
            .Select(vulnerability => ReadString(vulnerability, "id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();

        return new OsvBatchMatch(identifiers, IsTruncated: false);
    }

    /// <summary>
    /// Fetches the detail of every vulnerability the batch turned up and caches a result for each package.
    /// </summary>
    private async Task StoreBatchResultsAsync(PackageInfo[] batch, IReadOnlyList<OsvBatchMatch> matchesPerPackage)
    {
        string[] distinctIdentifiers = matchesPerPackage
            .Where(match => !match.IsTruncated)
            .SelectMany(match => match.Identifiers)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, OsvVulnerabilitySignals> signalsById = await FetchVulnerabilitiesAsync(distinctIdentifiers);

        foreach ((PackageInfo package, OsvBatchMatch match) in batch.Zip(matchesPerPackage))
        {
            StoreBatchResult(package, match, signalsById);
        }
    }

    /// <summary>
    /// Caches the assembled result of a single package, unless the batch could not answer for it in full.
    /// </summary>
    private static void StoreBatchResult(PackageInfo package, OsvBatchMatch match,
        IReadOnlyDictionary<string, OsvVulnerabilitySignals> signalsById)
    {
        if (match.IsTruncated)
        {
            return;
        }

        OsvVulnerabilitySignals[] signals = match.Identifiers
            .Select(signalsById.GetValueOrDefault)
            .Where(signal => signal is not null)
            .Select(signal => signal!)
            .ToArray();

        lock (CacheLock)
        {
            Cache[CreateCacheKey(package)] = BuildResult(signals);
        }
    }

    /// <summary>
    /// Fetches the detail of each vulnerability identifier, a handful at a time.
    /// </summary>
    private async Task<Dictionary<string, OsvVulnerabilitySignals>> FetchVulnerabilitiesAsync(string[] identifiers)
    {
        Dictionary<string, OsvVulnerabilitySignals> signalsById = new(StringComparer.Ordinal);
        foreach (string[] chunk in identifiers.Chunk(MaxConcurrentDetailRequests))
        {
            OsvVulnerabilitySignals?[] signals =
                await Task.WhenAll(chunk.Select(TryFetchVulnerabilityAsync));

            foreach ((string identifier, OsvVulnerabilitySignals? signal) in chunk.Zip(signals))
            {
                AddSignal(signalsById, identifier, signal);
            }
        }

        return signalsById;
    }

    /// <summary>
    /// Adds a fetched vulnerability to the lookup, ignoring the ones that could not be read.
    /// </summary>
    private static void AddSignal(Dictionary<string, OsvVulnerabilitySignals> signalsById, string identifier,
        OsvVulnerabilitySignals? signal)
    {
        if (signal is not null)
        {
            signalsById[identifier] = signal;
        }
    }

    /// <summary>
    /// Fetches the detail of a single vulnerability, returning <see langword="null"/> when it cannot be read.
    /// </summary>
    private async Task<OsvVulnerabilitySignals?> TryFetchVulnerabilityAsync(string identifier)
    {
        try
        {
            logger.LogDebug("Fetching OSV vulnerability {Identifier}", identifier);
            string url = $"https://api.osv.dev/v1/vulns/{Uri.EscapeDataString(identifier)}";
            using JsonDocument doc = JsonDocument.Parse(await httpClient.GetStringAsync(url));
            return ReadSignals(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogDebug(ex, "Could not fetch OSV vulnerability {Identifier}", identifier);
            return null;
        }
    }

    /// <summary>
    /// Pages through OSV API results for <paramref name="package"/>, aggregating severity and fix data into an <see cref="OsvPackageRiskResult"/>.
    /// </summary>
    private async Task<OsvPackageRiskResult> QueryAsync(PackageInfo package)
    {
        string? pageToken = null;
        List<OsvVulnerabilitySignals> signals = [];

        do
        {
            logger.LogDebug("Querying OSV API for {Name} {Version}", package.Name, package.Version);
            using JsonDocument doc =
                await PostAsync("https://api.osv.dev/v1/query", CreateQueryBody(package, pageToken));

            signals.AddRange(ReadResponseSignals(doc.RootElement));
            pageToken = ReadPageToken(doc.RootElement);
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return BuildResult(signals);
    }

    /// <summary>
    /// Reads the signals of every vulnerability in the <c>vulns</c> array of an OSV response.
    /// </summary>
    private static IEnumerable<OsvVulnerabilitySignals> ReadResponseSignals(JsonElement root)
    {
        if (!root.TryGetProperty("vulns", out JsonElement vulnerabilities) ||
            vulnerabilities.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return vulnerabilities.EnumerateArray().Select(ReadSignals).ToArray();
    }

    /// <summary>
    /// Reads everything the risk metrics need from a single OSV vulnerability entry, so that the entry itself does not
    /// have to be kept around.
    /// </summary>
    private static OsvVulnerabilitySignals ReadSignals(JsonElement vulnerability)
    {
        double severity = ReadSeverity(vulnerability);
        bool hasFix = HasFix(vulnerability);

        return new OsvVulnerabilitySignals(
            hasFix,
            hasFix && IsRecentlyModified(vulnerability),
            TryGetDaysToFix(vulnerability),
            ReadVulnerabilityRecord(vulnerability, severity));
    }

    /// <summary>
    /// Aggregates the signals of a package's vulnerabilities into its risk result.
    /// </summary>
    private static OsvPackageRiskResult BuildResult(IReadOnlyCollection<OsvVulnerabilitySignals> signals) => new()
    {
        VulnerabilityCount = signals.Count,
        MaxSeverity = signals.Count == 0 ? 0 : signals.Max(signal => signal.Record.Severity),
        HasPatchedVulnerabilityInLast90Days = signals.Any(signal => signal.WasPatchedRecently),
        HasAvailableSecurityFix = signals.Any(signal => signal.HasKnownFix),
        MedianVulnerabilityFixDays = ComputeMedian(signals
            .Where(signal => signal.DaysToFix.HasValue)
            .Select(signal => signal.DaysToFix!.Value)
            .ToList()),
        Vulnerabilities = signals.Select(signal => signal.Record).ToArray()
    };

    /// <summary>
    /// Reads the continuation token of a paged OSV response, if any.
    /// </summary>
    private static string? ReadPageToken(JsonElement root) =>
        root.TryGetProperty("next_page_token", out JsonElement pageToken) ? pageToken.GetString() : null;

    /// <summary>
    /// Posts a JSON body to the OSV API and returns the parsed response.
    /// </summary>
    private async Task<JsonDocument> PostAsync(string url, string body)
    {
        using StringContent content = new(body, Encoding.UTF8, "application/json");
        using HttpRequestMessage request = new(HttpMethod.Post, url);
        request.Content = content;

        using HttpResponseMessage response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Returns the OSV ecosystem name for a package.
    /// </summary>
    private static string GetEcosystem(PackageInfo package) => package.Source switch
    {
        "npm" => "npm",
        _ => "NuGet"
    };

    /// <summary>
    /// Extracts the identifier, aliases, severity, and reference URLs for a single OSV vulnerability entry.
    /// </summary>
    private static OsvVulnerabilityRecord ReadVulnerabilityRecord(JsonElement vulnerability, double severity)
    {
        return new OsvVulnerabilityRecord
        {
            Id = ReadString(vulnerability, "id") ?? "",
            Aliases = ReadStringArray(vulnerability, "aliases"),
            Severity = severity,
            References = ReadReferenceUrls(vulnerability)
        };
    }

    /// <summary>
    /// Reads the string-valued property identified by <paramref name="propertyName"/> from <paramref name="element"/>;
    /// returns <see langword="null"/> if the property is absent.
    /// </summary>
    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) ? property.GetString() : null;
    }

    /// <summary>
    /// Reads the string-array property identified by <paramref name="propertyName"/> from <paramref name="element"/>,
    /// dropping any null entries; returns an empty array if the property is absent.
    /// </summary>
    private static string[] ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.GetString())
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    /// <summary>
    /// Extracts the "url" of every entry in the vulnerability's "references" array, dropping entries without one.
    /// </summary>
    private static string[] ReadReferenceUrls(JsonElement vulnerability)
    {
        if (!vulnerability.TryGetProperty("references", out JsonElement references) || references.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return references.EnumerateArray()
            .Select(reference => reference.TryGetProperty("url", out JsonElement urlElement) ? urlElement.GetString() : null)
            .Where(url => url is not null)
            .Select(url => url!)
            .ToArray();
    }

    /// <summary>
    /// Builds the JSON request body for the OSV query API, optionally including a pagination token.
    /// </summary>
    private static string CreateQueryBody(PackageInfo package, string? pageToken)
    {
        string pagination = string.IsNullOrWhiteSpace(pageToken)
            ? string.Empty
            : $$""","page_token":"{{Escape(pageToken)}}" """.TrimEnd();

        return $$"""{"package":{"name":"{{Escape(package.Name)}}","ecosystem":"{{GetEcosystem(package)}}"},"version":"{{Escape(package.Version)}}"{{pagination}}}""";
    }

    /// <summary>
    /// Builds the JSON object identifying one package version within a batch query.
    /// </summary>
    private static string CreateQuery(PackageInfo package) =>
        $$"""{"package":{"name":"{{Escape(package.Name)}}","ecosystem":"{{GetEcosystem(package)}}"},"version":"{{Escape(package.Version)}}"}""";

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
        package.Vulnerabilities = result.Vulnerabilities.ToArray();
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
                double score = ParseScore(scoreElement);
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
    /// Yields text severity strings sourced from top-level and affected-entry <c>ecosystem_specific</c> /
    /// <c>database_specific</c> metadata.
    /// </summary>
    private static IEnumerable<string> EnumerateTextSeverities(JsonElement vulnerability)
    {
        if (TryReadTextSeverity(vulnerability, "ecosystem_specific", out string ecosystemSeverity))
        {
            yield return ecosystemSeverity;
        }

        if (TryReadTextSeverity(vulnerability, "database_specific", out string databaseSeverity))
        {
            yield return databaseSeverity;
        }

        if (!vulnerability.TryGetProperty("affected", out JsonElement affected) || affected.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement affectedPackage in affected.EnumerateArray())
        {
            if (TryReadTextSeverity(affectedPackage, "ecosystem_specific", out string affectedEcosystemSeverity))
            {
                yield return affectedEcosystemSeverity;
            }

            if (TryReadTextSeverity(affectedPackage, "database_specific", out string affectedDatabaseSeverity))
            {
                yield return affectedDatabaseSeverity;
            }
        }
    }

    /// <summary>
    /// Reads a text severity value from an <c>ecosystem_specific</c> or <c>database_specific</c> object, when present.
    /// </summary>
    private static bool TryReadTextSeverity(JsonElement element, string propertyName, out string severity)
    {
        severity = string.Empty;

        if (!element.TryGetProperty(propertyName, out JsonElement nestedObject) || nestedObject.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!nestedObject.TryGetProperty("severity", out JsonElement severityValue))
        {
            return false;
        }

        string? value = severityValue.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        severity = value;
        return true;
    }

    /// <summary>
    /// Parses a CVSS vector string or plain numeric score into a <see cref="double"/>; returns <c>0</c> when parsing fails.
    /// </summary>
    private static double ParseScore(JsonElement scoreElement)
    {
        if (scoreElement.ValueKind == JsonValueKind.Number && scoreElement.TryGetDouble(out double numericScore))
        {
            return numericScore;
        }

        string? score = scoreElement.ValueKind == JsonValueKind.String ? scoreElement.GetString() : null;
        return ParseScore(score);
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
        return parts.Select(part =>
                double.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out double value) ? value : 0)
            .FirstOrDefault(value => value > 0);
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
    /// Everything the risk metrics need from a single vulnerability, read once so that the OSV response it came from
    /// does not have to be kept alive.
    /// </summary>
    /// <param name="HasKnownFix">Indicates whether a fixed version is known.</param>
    /// <param name="WasPatchedRecently">Indicates whether a fix landed within the last 90 days.</param>
    /// <param name="DaysToFix">The number of days between publication and fix, when both are known.</param>
    /// <param name="Record">The vulnerability as it is reported to the user.</param>
    private sealed record OsvVulnerabilitySignals(
        bool HasKnownFix,
        bool WasPatchedRecently,
        double? DaysToFix,
        OsvVulnerabilityRecord Record);

    /// <summary>
    /// What the batch endpoint reported for a single package.
    /// </summary>
    /// <param name="Identifiers">The identifiers of the vulnerabilities that affect the package.</param>
    /// <param name="IsTruncated">
    /// Indicates that the endpoint could not answer in full, in which case the package is looked up again through the
    /// paging single-package query rather than being reported with an incomplete count.
    /// </param>
    private sealed record OsvBatchMatch(string[] Identifiers, bool IsTruncated);

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

        /// <summary>
        /// The individual vulnerability records found for the package.
        /// </summary>
        public IReadOnlyList<OsvVulnerabilityRecord> Vulnerabilities { get; init; } = [];
    }
}
