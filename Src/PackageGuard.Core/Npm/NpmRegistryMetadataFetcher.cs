using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using PackageGuard.Core.Common;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Fetches license, license URL, and repository URL information from the NPM registry.
/// </summary>
public class NpmRegistryMetadataFetcher
{
    /// <summary>
    /// The shared HTTP client used to query the NPM registry.
    /// </summary>
    private static readonly HttpClient SharedHttpClient = new();

    private readonly ILogger logger;
    private readonly HttpClient httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="NpmRegistryMetadataFetcher"/> class.
    /// </summary>
    /// <param name="logger">The logger to report fetch problems to.</param>
    public NpmRegistryMetadataFetcher(ILogger logger) : this(logger, SharedHttpClient)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NpmRegistryMetadataFetcher"/> class that sends its requests
    /// through the given client.
    /// </summary>
    /// <param name="logger">The logger to report fetch problems to.</param>
    /// <param name="httpClient">The client to send registry requests through.</param>
    internal NpmRegistryMetadataFetcher(ILogger logger, HttpClient httpClient)
    {
        this.logger = logger;
        this.httpClient = httpClient;
    }

    /// <summary>
    /// Registry responses already received during this run, keyed by registry URL. A package that is referenced at
    /// more than one version otherwise downloads the same document once per version.
    /// </summary>
    private static readonly Dictionary<string, string> PackumentsByUrl = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Guards access to <see cref="PackumentsByUrl"/>.
    /// </summary>
    private static readonly Lock PackumentLock = new();

    /// <summary>
    /// The packages whose download count still has to be looked up.
    /// </summary>
    private readonly List<PackageInfo> pendingDownloadCounts = [];

    /// <summary>
    /// Guards access to <see cref="pendingDownloadCounts"/>.
    /// </summary>
    private readonly Lock pendingDownloadCountsLock = new();

    /// <summary>
    /// The number of package names the NPM downloads API accepts in a single bulk lookup.
    /// </summary>
    private const int MaxNamesPerBulkLookup = 128;

    /// <summary>
    /// The number of scoped download-count lookups that are in flight at the same time.
    /// </summary>
    private const int MaxConcurrentScopedLookups = 8;

    /// <summary>
    /// One or more NuGet or NPM feeds that should be completely ignored during the analysis.
    /// </summary>
    /// <value>
    /// Each feed is wildcard string that can match the NPM or NuGet feed name or URL.
    /// </value>
    public string[] IgnoredFeeds { get; set; } = [];

    /// <summary>
    /// Fetches and populates license, repository URL, deprecation status, version, and download count metadata
    /// for the given npm package by querying the NPM registry.
    /// </summary>
    /// <param name="package">The package whose metadata should be enriched.</param>
    public async Task FetchMetadataAsync(PackageInfo package)
    {
        // Only process packages from npm source
        if (package.Source != "npm")
        {
            return;
        }

        try
        {
            // Extract the registry URL from the package's SourceUrl (resolved field)
            // This supports both public npmjs.org and private npm registries
            string registryUrl = GetRegistryUrl(package);

            // Skip this feed if it's in the ignored list
            if (registryUrl.MatchesAnyWildcard(IgnoredFeeds))
            {
                logger.LogDebug("Ignoring feed {Url}", registryUrl);
                return;
            }

            string jsonContent = await GetPackumentAsync(registryUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            ParseVersionMetadata(package, root);
            ParsePackageMetadata(package, root);

            QueueDownloadCount(package);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Failed to fetch NPM package metadata for {Name} {Version}: {Error}",
                package.Name, package.Version, ex.Message);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Failed to parse NPM package metadata for {Name} {Version}: {Error}",
                package.Name, package.Version, ex.Message);
        }
    }

    /// <summary>
    /// Returns the registry document for <paramref name="registryUrl"/>, downloading it only the first time it is
    /// asked for during this run.
    /// </summary>
    private async Task<string> GetPackumentAsync(string registryUrl)
    {
        lock (PackumentLock)
        {
            if (PackumentsByUrl.TryGetValue(registryUrl, out string? cached))
            {
                return cached;
            }
        }

        logger.LogDebug("Fetching NPM package metadata from {Url}", registryUrl);
        string jsonContent = await httpClient.GetStringAsync(registryUrl);

        lock (PackumentLock)
        {
            PackumentsByUrl[registryUrl] = jsonContent;
        }

        return jsonContent;
    }

    /// <summary>
    /// Discards the registry documents cached during this run. Only used by the tests.
    /// </summary>
    internal static void ClearPackumentCache()
    {
        lock (PackumentLock)
        {
            PackumentsByUrl.Clear();
        }
    }

    /// <summary>
    /// Records that the package still needs a download count, which is looked up in bulk afterwards.
    /// </summary>
    private void QueueDownloadCount(PackageInfo package)
    {
        lock (pendingDownloadCountsLock)
        {
            pendingDownloadCounts.Add(package);
        }
    }

    /// <summary>Parses version-specific metadata: published date, latest stable version, and version lag.</summary>
    private static void ParseVersionMetadata(PackageInfo package, JsonElement root)
    {
        JsonElement timeElement = root.TryGetProperty("time", out JsonElement parsedTimeElement) &&
                                  parsedTimeElement.ValueKind == JsonValueKind.Object
            ? parsedTimeElement
            : default;

        if (timeElement.ValueKind == JsonValueKind.Object &&
            timeElement.TryGetProperty(package.Version, out JsonElement publishedElement) &&
            DateTimeOffset.TryParse(publishedElement.GetString(), out DateTimeOffset publishedAt))
        {
            package.PublishedAt = publishedAt;
        }

        if (!root.TryGetProperty("dist-tags", out JsonElement distTagsElement) ||
            distTagsElement.ValueKind != JsonValueKind.Object ||
            !distTagsElement.TryGetProperty("latest", out JsonElement latestElement))
        {
            return;
        }

        string? latestStableVersion = latestElement.GetString();
        if (string.IsNullOrWhiteSpace(latestStableVersion))
        {
            return;
        }

        package.LatestStableVersion = latestStableVersion;

        if (timeElement.ValueKind == JsonValueKind.Object &&
            timeElement.TryGetProperty(latestStableVersion, out JsonElement latestPublishedElement) &&
            DateTimeOffset.TryParse(latestPublishedElement.GetString(), out DateTimeOffset latestStablePublishedAt))
        {
            package.LatestStablePublishedAt = latestStablePublishedAt;
        }

        if (TryParseSemanticVersion(latestStableVersion, out NuGetVersion? latestVersion) &&
            TryParseSemanticVersion(package.Version, out NuGetVersion? currentVersion))
        {
            package.IsMajorVersionBehindLatest = latestVersion is not null &&
                                                 currentVersion is not null &&
                                                 latestVersion.Major > currentVersion.Major;

            package.IsMinorVersionBehindLatest = latestVersion is not null &&
                                                 currentVersion is not null &&
                                                 latestVersion.Major == currentVersion.Major &&
                                                 latestVersion > currentVersion;
        }

        if (package is { PublishedAt: not null, LatestStablePublishedAt: not null } &&
            package.LatestStablePublishedAt.Value > package.PublishedAt.Value)
        {
            package.VersionUpdateLagDays =
                (package.LatestStablePublishedAt.Value - package.PublishedAt.Value).TotalDays;
        }
    }

    /// <summary>Parses license, repository URL, deprecation status, and license URL from the registry response.</summary>
    private void ParsePackageMetadata(PackageInfo package, JsonElement root)
    {
        JsonElement currentVersionMetadata = TryGetCurrentVersionMetadata(root, package.Version);

        // Extract license if not already present
        if (package.License is null)
        {
            if (currentVersionMetadata.ValueKind == JsonValueKind.Object &&
                currentVersionMetadata.TryGetProperty("license", out JsonElement currentLicenseElement))
            {
                package.License = currentLicenseElement.GetString();
            }
            else if (root.TryGetProperty("license", out JsonElement licenseElement))
            {
                package.License = licenseElement.GetString();
            }

            if (package.License is not null)
            {
                package.LicenseEvidence = LicenseEvidence.Declared;
            }

            logger.LogDebug("Found license for {Name}: {License}", package.Name, package.License);
        }

        // Extract repository URL if not already present
        if (package.RepositoryUrl is null)
        {
            JsonElement repositoryElement = currentVersionMetadata.ValueKind == JsonValueKind.Object &&
                                            currentVersionMetadata.TryGetProperty("repository",
                                                out JsonElement versionRepositoryElement)
                ? versionRepositoryElement
                : root.TryGetProperty("repository", out JsonElement rootRepositoryElement)
                    ? rootRepositoryElement
                    : default;

            if (repositoryElement.ValueKind == JsonValueKind.String)
            {
                package.RepositoryUrl = repositoryElement.GetString();
            }
            else if (repositoryElement.ValueKind == JsonValueKind.Object &&
                     repositoryElement.TryGetProperty("url", out JsonElement urlElement))
            {
                string? repoUrl = urlElement.GetString();
                if (repoUrl is not null)
                {
                    // Clean up git+ prefix and .git suffix if present
                    package.RepositoryUrl = repoUrl
                        .Replace("git+", "")
                        .Replace("git://", "https://")
                        .TrimEnd('/', '.', 'g', 'i', 't');
                }
            }

            logger.LogDebug("Found repository URL for {Name}: {Url}", package.Name, package.RepositoryUrl);
        }

        if (currentVersionMetadata.ValueKind == JsonValueKind.Object &&
            currentVersionMetadata.TryGetProperty("deprecated", out JsonElement deprecatedElement))
        {
            package.IsDeprecated = !string.IsNullOrWhiteSpace(deprecatedElement.GetString());
        }

        // Extract license URL if available (some packages have this)
        if (package.LicenseUrl is null)
        {
            // Try to construct a license URL from the repository
            if (package.RepositoryUrl is not null && package.RepositoryUrl.Contains("github.com"))
            {
                // Construct a typical GitHub license URL
                string cleanUrl = package.RepositoryUrl.TrimEnd('/');
                package.LicenseUrl = $"{cleanUrl}/blob/master/LICENSE";
                logger.LogDebug("Constructed license URL for {Name}: {Url}", package.Name, package.LicenseUrl);
            }
        }
    }

    /// <summary>
    /// Extracts the registry base URL from the package's SourceUrl (resolved field from package-lock.json).
    /// This supports both public npm registry and private registries.
    /// </summary>
    private string GetRegistryUrl(PackageInfo package)
    {
        string sourceUrl = package.SourceUrl;

        // If SourceUrl is the default fallback, use public registry
        if (sourceUrl == "https://registry.npmjs.org")
        {
            return $"{sourceUrl}/{package.Name}";
        }

        // Parse the resolved URL to extract the registry base URL
        // Format is typically: https://registry.example.com/package-name/-/package-name-version.tgz
        // We need to construct: https://registry.example.com/package-name/version

        try
        {
            Uri uri = new(sourceUrl);
            string registryBase = $"{uri.Scheme}://{uri.Host}";

            // Add port if not default
            if (uri.Port != 80 && uri.Port != 443 && uri.Port != -1)
            {
                registryBase += $":{uri.Port}";
            }

            // Add path prefix if present (for registries hosted under a subpath)
            string[] pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Find where the package name starts in the path
            // Common patterns:
            // - /package-name/-/package-name-version.tgz (public npm)
            // - /path/to/registry/package-name/-/package-name-version.tgz (private with path)
            // - /@scope/package-name/-/package-name-version.tgz (scoped package)

            int packageIndex = -1;
            string packageNameInUrl = package.Name.Replace("/", "%2f"); // Scoped packages may be URL-encoded
            for (int i = 0; i < pathSegments.Length; i++)
            {
                string decodedSegment = Uri.UnescapeDataString(pathSegments[i]);
                if (decodedSegment == package.Name || decodedSegment == packageNameInUrl)
                {
                    packageIndex = i;
                    break;
                }

                // For scoped packages, check if this segment is the scope
                if (package.Name.StartsWith("@") && decodedSegment.StartsWith("@"))
                {
                    string scope = package.Name.Split('/')[0];
                    if (decodedSegment == scope && i + 1 < pathSegments.Length)
                    {
                        string packagePart = package.Name.Split('/')[1];
                        string nextSegment = Uri.UnescapeDataString(pathSegments[i + 1]);
                        if (nextSegment == packagePart)
                        {
                            packageIndex = i;
                            break;
                        }
                    }
                }
            }

            // Add any path prefix before the package name
            if (packageIndex > 0)
            {
                for (int i = 0; i < packageIndex; i++)
                {
                    registryBase += "/" + pathSegments[i];
                }
            }

            // Construct the final metadata URL
            return $"{registryBase}/{package.Name}";
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to parse registry URL from {SourceUrl}, falling back to public npm registry: {Error}",
                sourceUrl, ex.Message);

            // Fallback to public npm registry
            return $"https://registry.npmjs.org/{package.Name}";
        }
    }

    /// <summary>
    /// Tries to retrieve the version-specific metadata element from the registry response,
    /// falling back to the root element when the specific version entry is absent.
    /// </summary>
    /// <param name="root">The root JSON element of the registry response.</param>
    /// <param name="version">The package version to look up.</param>
    /// <returns>The version-level <see cref="JsonElement"/>, or <paramref name="root"/> when not found.</returns>
    private static JsonElement TryGetCurrentVersionMetadata(JsonElement root, string version)
    {
        if (root.TryGetProperty("versions", out JsonElement versionsElement) &&
            versionsElement.ValueKind == JsonValueKind.Object &&
            versionsElement.TryGetProperty(version, out JsonElement versionElement))
        {
            return versionElement;
        }

        return root;
    }

    /// <summary>
    /// Populates <see cref="PackageInfo.DownloadCount"/> for every package whose metadata was fetched, asking the NPM
    /// downloads API for up to <see cref="MaxNamesPerBulkLookup"/> packages at a time.
    /// </summary>
    /// <remarks>
    /// The downloads API takes a comma-separated list of names, which turns one request per package into one request
    /// per 128. Scoped names are not accepted in a bulk lookup and are still asked for one at a time.
    /// </remarks>
    public async Task ResolveDownloadCountsAsync()
    {
        PackageInfo[] pending = TakePendingDownloadCounts();
        if (pending.Length == 0)
        {
            return;
        }

        logger.LogDebug("Fetching download counts for {Count} NPM packages", pending.Length);

        await ResolveBulkDownloadCountsAsync(pending.Where(package => !IsScoped(package.Name)).ToArray());
        await ResolveScopedDownloadCountsAsync(pending.Where(package => IsScoped(package.Name)).ToArray());
    }

    /// <summary>
    /// Removes and returns the packages that are still waiting for a download count.
    /// </summary>
    private PackageInfo[] TakePendingDownloadCounts()
    {
        lock (pendingDownloadCountsLock)
        {
            PackageInfo[] pending = pendingDownloadCounts.ToArray();
            pendingDownloadCounts.Clear();
            return pending;
        }
    }

    /// <summary>
    /// Looks up the unscoped packages in batches, keyed by name in the response.
    /// </summary>
    private async Task ResolveBulkDownloadCountsAsync(PackageInfo[] packages)
    {
        foreach (PackageInfo[] batch in packages.Chunk(MaxNamesPerBulkLookup))
        {
            string names = string.Join(",", batch.Select(package => Uri.EscapeDataString(package.Name)));
            using JsonDocument? doc = await TryGetDownloadsAsync(names);
            if (doc is null)
            {
                continue;
            }

            ApplyBulkDownloadCounts(batch, doc.RootElement);
        }
    }

    /// <summary>
    /// Reads each package's entry out of a bulk downloads response. A single-name batch answers without the name
    /// wrapper, so that shape is handled as well.
    /// </summary>
    private static void ApplyBulkDownloadCounts(PackageInfo[] batch, JsonElement root)
    {
        if (batch.Length == 1)
        {
            ApplyDownloadCount(batch[0], root);
            return;
        }

        foreach (PackageInfo package in batch)
        {
            if (root.TryGetProperty(package.Name, out JsonElement entry) && entry.ValueKind == JsonValueKind.Object)
            {
                ApplyDownloadCount(package, entry);
            }
        }
    }

    /// <summary>
    /// Looks up the scoped packages, which the downloads API only answers for one at a time.
    /// </summary>
    private async Task ResolveScopedDownloadCountsAsync(PackageInfo[] packages)
    {
        foreach (PackageInfo[] chunk in packages.Chunk(MaxConcurrentScopedLookups))
        {
            await Task.WhenAll(chunk.Select(ResolveScopedDownloadCountAsync));
        }
    }

    /// <summary>
    /// Looks up the download count of a single scoped package.
    /// </summary>
    private async Task ResolveScopedDownloadCountAsync(PackageInfo package)
    {
        using JsonDocument? doc = await TryGetDownloadsAsync(package.Name);
        if (doc is not null)
        {
            ApplyDownloadCount(package, doc.RootElement);
        }
    }

    /// <summary>
    /// Requests the last-month downloads for one or more package names, returning <see langword="null"/> on failure.
    /// </summary>
    private async Task<JsonDocument?> TryGetDownloadsAsync(string names)
    {
        string downloadsUrl = $"https://api.npmjs.org/downloads/point/last-month/{names}";
        try
        {
            logger.LogDebug("Fetching download counts from {Url}", downloadsUrl);
            return JsonDocument.Parse(await httpClient.GetStringAsync(downloadsUrl));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to fetch download counts from {Url}", downloadsUrl);
            return null;
        }
    }

    /// <summary>
    /// Copies the <c>downloads</c> value of a downloads API entry onto the package.
    /// </summary>
    private static void ApplyDownloadCount(PackageInfo package, JsonElement entry)
    {
        if (entry.TryGetProperty("downloads", out JsonElement downloads) &&
            downloads.TryGetInt64(out long downloadCount))
        {
            package.DownloadCount = downloadCount;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> for an NPM name that carries a scope, such as <c>@babel/core</c>.
    /// </summary>
    private static bool IsScoped(string name) => name.StartsWith('@');

    /// <summary>
    /// Tries to parse a semantic version string, stripping a leading <c>v</c> prefix if present.
    /// </summary>
    /// <param name="value">The raw version string to parse.</param>
    /// <param name="version">When successful, contains the parsed <see cref="NuGetVersion"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    private static bool TryParseSemanticVersion(string value, out NuGetVersion? version)
    {
        string normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        return NuGetVersion.TryParse(normalized, out version);
    }
}
