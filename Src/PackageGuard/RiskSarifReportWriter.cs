using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using PackageGuard.Core;

namespace PackageGuard;

/// <summary>
/// Builds a SARIF 2.1.0 document that describes packages with medium or high risk scores so that
/// results can be consumed by GitHub Advanced Security and other SARIF-compatible tooling.
/// </summary>
internal static class RiskSarifReportWriter
{
    /// <summary>
    /// SARIF rule ID for packages with a medium overall risk score (30–59).
    /// </summary>
    private const string MediumRiskRuleId = "packageguard/risk-medium";

    /// <summary>
    /// SARIF rule ID for packages with a high overall risk score (60+).
    /// </summary>
    private const string HighRiskRuleId = "packageguard/risk-high";

    /// <summary>
    /// Builds a SARIF JSON string reporting all packages with a risk score of 30 or above,
    /// using the project at <paramref name="projectPath"/> as the artifact location reference.
    /// </summary>
    public static string BuildSarif(string projectPath, IReadOnlyCollection<PackageInfo> packages)
    {
        string locationPath = ResolveLocationPath(projectPath);

        var sarif = new SarifLog
        {
            Runs =
            [
                new SarifRun
                {
                    Tool = new SarifTool
                    {
                        Driver = new SarifDriver
                        {
                            Name = "PackageGuard",
                            InformationUri = "https://github.com/dennisdoomen/packageguard",
                            Rules =
                            [
                                CreateRule(
                                    MediumRiskRuleId,
                                    "Medium package risk",
                                    "PackageGuard identified a package with a medium overall risk score.",
                                    "6.0"),
                                CreateRule(
                                    HighRiskRuleId,
                                    "High package risk",
                                    "PackageGuard identified a package with a high overall risk score.",
                                    "9.0")
                            ]
                        }
                    },
                    Results = packages
                        .Where(package => package.RiskScore >= 30)
                        .Select(package => CreateResult(locationPath, package))
                        .ToArray()
                }
            ]
        };

        return JsonSerializer.Serialize(sarif, SerializerOptions);
    }

    /// <summary>
    /// Creates a SARIF <c>reportingDescriptor</c> rule with the given ID, name, description, and CVSS-style security severity score.
    /// </summary>
    private static SarifReportingDescriptor CreateRule(string id, string name, string description, string securitySeverity)
    {
        return new SarifReportingDescriptor
        {
            Id = id,
            Name = name,
            ShortDescription = new SarifMessage { Text = description },
            FullDescription = new SarifMessage
            {
                Text = "Review the companion HTML report for the full PackageGuard rationale and evidence."
            },
            HelpUri = "https://github.com/dennisdoomen/packageguard",
            Properties = new Dictionary<string, string>
            {
                ["security-severity"] = securitySeverity
            }
        };
    }

    /// <summary>
    /// Creates a SARIF result entry for a single package, including risk message, fingerprint, properties, and location.
    /// </summary>
    private static SarifResult CreateResult(string locationPath, PackageInfo package)
    {
        string riskZone = package.RiskScore >= 60 ? "High" : "Medium";
        string ruleId = riskZone == "High" ? HighRiskRuleId : MediumRiskRuleId;

        return new SarifResult
        {
            RuleId = ruleId,
            Level = riskZone == "High" ? "error" : "warning",
            Message = new SarifMessage
            {
                Text =
                    $"{package.Name} {package.Version} scored {package.RiskScore.ToString("0.0", CultureInfo.InvariantCulture)}/100 ({riskZone}). " +
                    $"Legal {package.RiskDimensions.LegalRisk.ToString("0.0", CultureInfo.InvariantCulture)}/10, " +
                    $"Security {package.RiskDimensions.SecurityRisk.ToString("0.0", CultureInfo.InvariantCulture)}/10, " +
                    $"Operational {package.RiskDimensions.OperationalRisk.ToString("0.0", CultureInfo.InvariantCulture)}/10. " +
                    $"Used by: {string.Join(", ", GetDisplayProjectPaths(package))}."
            },
            PartialFingerprints = new Dictionary<string, string>
            {
                ["package"] = $"{package.Source}|{package.Name}|{package.Version}"
            },
            Properties = new Dictionary<string, object>
            {
                ["packageName"] = package.Name,
                ["packageVersion"] = package.Version,
                ["source"] = package.Source,
                ["usedBy"] = GetDisplayProjectPaths(package),
                ["riskScore"] = package.RiskScore,
                ["riskZone"] = riskZone,
                ["legalRisk"] = package.RiskDimensions.LegalRisk,
                ["securityRisk"] = package.RiskDimensions.SecurityRisk,
                ["operationalRisk"] = package.RiskDimensions.OperationalRisk,
                ["legalRationale"] = package.RiskDimensions.LegalRiskRationale,
                ["securityRationale"] = package.RiskDimensions.SecurityRiskRationale,
                ["operationalRationale"] = package.RiskDimensions.OperationalRiskRationale
            },
            Locations =
            [
                new SarifLocation
                {
                    PhysicalLocation = new SarifPhysicalLocation
                    {
                        ArtifactLocation = new SarifArtifactLocation { Uri = locationPath },
                        Region = new SarifRegion { StartLine = 1 }
                    }
                }
            ]
        };
    }

    /// <summary>
    /// Resolves a SARIF artifact URI path from the given <paramref name="projectPath"/>, preferring
    /// well-known config files, solution files, or the path itself as a fallback.
    /// </summary>
    private static string ResolveLocationPath(string projectPath)
    {
        string rootPath = ResolveRootPath(projectPath);

        if (File.Exists(projectPath))
        {
            return ToSarifUri(rootPath, projectPath);
        }

        if (!Directory.Exists(projectPath))
        {
            return ToSarifUri(rootPath, projectPath);
        }

        string[] preferredFiles =
        [
            Path.Combine(projectPath, ".packageguard", "config.json"),
            Path.Combine(projectPath, "packageguard.config.json")
        ];

        string? preferred = preferredFiles.FirstOrDefault(File.Exists);
        if (preferred is not null)
        {
            return ToSarifUri(rootPath, preferred);
        }

        string? projectFile = Directory.EnumerateFiles(projectPath, "*.sln").FirstOrDefault()
            ?? Directory.EnumerateFiles(projectPath, "*.slnx").FirstOrDefault()
            ?? Directory.EnumerateFiles(projectPath, "*.csproj").FirstOrDefault()
            ?? Directory.EnumerateFiles(projectPath, "package.json").FirstOrDefault();

        return ToSarifUri(rootPath, projectFile ?? projectPath);
    }

    /// <summary>
    /// Returns the absolute root directory to use when computing relative SARIF URIs.
    /// </summary>
    private static string ResolveRootPath(string projectPath)
    {
        if (Directory.Exists(projectPath))
        {
            return Path.GetFullPath(projectPath);
        }

        string? directory = Path.GetDirectoryName(projectPath);
        return string.IsNullOrWhiteSpace(directory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(directory);
    }

    /// <summary>
    /// Converts an absolute file path to a SARIF URI, using a relative URI when the path falls under
    /// <paramref name="rootPath"/>, or an absolute <c>file://</c> URI otherwise.
    /// </summary>
    private static string ToSarifUri(string rootPath, string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (TryCreateRelativeUri(rootPath, fullPath, out string? relativeUri) && relativeUri is not null)
        {
            return relativeUri;
        }

        return new Uri(fullPath).AbsoluteUri;
    }

    /// <summary>
    /// Attempts to compute a forward-slash relative URI from <paramref name="rootPath"/> to
    /// <paramref name="fullPath"/>. Returns <c>false</c> when the path escapes the root.
    /// </summary>
    private static bool TryCreateRelativeUri(string rootPath, string fullPath, out string? relativeUri)
    {
        relativeUri = null;

        string relativePath = Path.GetRelativePath(rootPath, fullPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        relativeUri = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        return true;
    }

    /// <summary>
    /// Returns a deduplicated, sorted array of display-friendly project paths for the given package.
    /// </summary>
    private static string[] GetDisplayProjectPaths(PackageInfo package)
    {
        return package.Projects
            .Select(path => ToDisplayProjectPath(package, path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Normalises a raw project path into a display-friendly path, appending <c>package.json</c> for npm packages
    /// that reference a directory rather than a file.
    /// </summary>
    private static string ToDisplayProjectPath(PackageInfo package, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return string.Empty;
        }

        if (!package.Source.Equals("npm", StringComparison.OrdinalIgnoreCase))
        {
            return projectPath;
        }

        if (projectPath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
        {
            return projectPath;
        }

        return Path.Combine(projectPath, "package.json");
    }

    /// <summary>
    /// Shared JSON serializer options: null properties are omitted and output is indented.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    /// <summary>
    /// Root object of a SARIF 2.1.0 document, containing one or more analysis runs.
    /// </summary>
    private sealed class SarifLog
    {
        /// <summary>Gets the JSON Schema URI that identifies this document as SARIF 2.1.0.</summary>
        [JsonPropertyName("$schema")]
        public string Schema { get; init; } = "https://json.schemastore.org/sarif-2.1.0.json";

        /// <summary>Gets the SARIF format version.</summary>
        [JsonPropertyName("version")]
        public string Version { get; init; } = "2.1.0";

        /// <summary>Gets the analysis runs contained in this log.</summary>
        [JsonPropertyName("runs")]
        public SarifRun[] Runs { get; init; } = [];
    }

    /// <summary>
    /// Represents a single tool run within a SARIF log, containing the tool description and its results.
    /// </summary>
    private sealed class SarifRun
    {
        /// <summary>Gets the tool that produced this run.</summary>
        [JsonPropertyName("tool")]
        public SarifTool Tool { get; init; } = new();

        /// <summary>Gets the individual findings produced by this run.</summary>
        [JsonPropertyName("results")]
        public SarifResult[] Results { get; init; } = [];
    }

    /// <summary>
    /// Describes the static analysis tool that produced a SARIF run.
    /// </summary>
    private sealed class SarifTool
    {
        /// <summary>Gets the driver component that describes the tool's primary executable.</summary>
        [JsonPropertyName("driver")]
        public SarifDriver Driver { get; init; } = new();
    }

    /// <summary>
    /// Describes the primary executable component of a SARIF tool, including its rules.
    /// </summary>
    private sealed class SarifDriver
    {
        /// <summary>Gets the tool name shown in SARIF viewers.</summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        /// <summary>Gets the URL of the tool's home page or documentation.</summary>
        [JsonPropertyName("informationUri")]
        public string? InformationUri { get; init; }

        /// <summary>Gets the rule descriptors defined by this driver.</summary>
        [JsonPropertyName("rules")]
        public SarifReportingDescriptor[] Rules { get; init; } = [];
    }

    /// <summary>
    /// Describes a single SARIF rule, including its ID, human-readable descriptions, and severity metadata.
    /// </summary>
    private sealed class SarifReportingDescriptor
    {
        /// <summary>Gets the stable rule identifier (e.g. <c>packageguard/risk-high</c>).</summary>
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        /// <summary>Gets the human-readable rule name.</summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        /// <summary>Gets a short one-sentence description of the rule.</summary>
        [JsonPropertyName("shortDescription")]
        public SarifMessage ShortDescription { get; init; } = new();

        /// <summary>Gets a longer description of what the rule detects.</summary>
        [JsonPropertyName("fullDescription")]
        public SarifMessage FullDescription { get; init; } = new();

        /// <summary>Gets a URL where consumers can learn more about this rule.</summary>
        [JsonPropertyName("helpUri")]
        public string? HelpUri { get; init; }

        /// <summary>Gets optional rule properties such as <c>security-severity</c>.</summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, string>? Properties { get; init; }
    }

    /// <summary>
    /// Represents a single finding produced by a SARIF run, identifying the violated rule and affected location.
    /// </summary>
    private sealed class SarifResult
    {
        /// <summary>Gets the ID of the rule that this result violates.</summary>
        [JsonPropertyName("ruleId")]
        public string RuleId { get; init; } = "";

        /// <summary>Gets the SARIF severity level (<c>error</c> for high risk, <c>warning</c> for medium risk).</summary>
        [JsonPropertyName("level")]
        public string Level { get; init; } = "";

        /// <summary>Gets the human-readable message describing why this package was flagged.</summary>
        [JsonPropertyName("message")]
        public SarifMessage Message { get; init; } = new();

        /// <summary>Gets stable fingerprints used to de-duplicate results across runs.</summary>
        [JsonPropertyName("partialFingerprints")]
        public Dictionary<string, string>? PartialFingerprints { get; init; }

        /// <summary>Gets additional structured metadata about the package risk dimensions.</summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, object>? Properties { get; init; }

        /// <summary>Gets the source locations associated with this finding.</summary>
        [JsonPropertyName("locations")]
        public SarifLocation[] Locations { get; init; } = [];
    }

    /// <summary>
    /// A SARIF message object containing a plain-text string.
    /// </summary>
    private sealed class SarifMessage
    {
        /// <summary>Gets the plain-text message content.</summary>
        [JsonPropertyName("text")]
        public string Text { get; init; } = "";
    }

    /// <summary>
    /// Identifies where in the scanned project a SARIF result was found.
    /// </summary>
    private sealed class SarifLocation
    {
        /// <summary>Gets the physical file location for this result.</summary>
        [JsonPropertyName("physicalLocation")]
        public SarifPhysicalLocation PhysicalLocation { get; init; } = new();
    }

    /// <summary>
    /// Combines an artifact location (file URI) with a region (line number) to pinpoint a finding.
    /// </summary>
    private sealed class SarifPhysicalLocation
    {
        /// <summary>Gets the URI of the artifact (file) associated with this location.</summary>
        [JsonPropertyName("artifactLocation")]
        public SarifArtifactLocation ArtifactLocation { get; init; } = new();

        /// <summary>Gets the region within the artifact where the finding is reported.</summary>
        [JsonPropertyName("region")]
        public SarifRegion Region { get; init; } = new();
    }

    /// <summary>
    /// Holds the URI of the artifact (file) referenced by a SARIF physical location.
    /// </summary>
    private sealed class SarifArtifactLocation
    {
        /// <summary>Gets the relative or absolute URI of the referenced file.</summary>
        [JsonPropertyName("uri")]
        public string Uri { get; init; } = "";
    }

    /// <summary>
    /// Identifies a region within a source artifact by start line.
    /// </summary>
    private sealed class SarifRegion
    {
        /// <summary>Gets the one-based line number where the finding begins.</summary>
        [JsonPropertyName("startLine")]
        public int StartLine { get; init; }
    }
}
