using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Sbom;

/// <summary>
/// Builds an SPDX 2.3 JSON Software Bill of Materials document from a shared <see cref="SbomModel"/>.
/// </summary>
internal static class SpdxSbomWriter
{
    /// <summary>
    /// SPDX element ID for the synthetic root package that represents the analyzed solution.
    /// </summary>
    private const string RootPackageId = "SPDXRef-Package-root";

    /// <summary>
    /// Builds the SPDX JSON document for <paramref name="model"/>.
    /// </summary>
    public static string Build(SbomModel model)
    {
        IReadOnlyDictionary<string, string> spdxIdsByKey = model.Components
            .Select((component, index) => (component.Key, Id: $"SPDXRef-Package-{index}"))
            .ToDictionary(entry => entry.Key, entry => entry.Id, StringComparer.OrdinalIgnoreCase);

        var document = new SpdxDocument
        {
            Name = model.Root.Name,
            DocumentNamespace = $"https://packageguard/spdxdocs/{Uri.EscapeDataString(model.Root.Name)}-{Guid.NewGuid()}",
            CreationInfo = new SpdxCreationInfo
            {
                Created = FormatSpdxTimestamp(model.GeneratedAt),
                Creators = ["Tool: PackageGuard"]
            },
            Packages = BuildPackages(model, spdxIdsByKey),
            Relationships = BuildRelationships(model, spdxIdsByKey),
            Comment = BuildComment(model)
        };

        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    /// <summary>
    /// Builds the synthetic root package followed by one package entry per component.
    /// </summary>
    private static SpdxPackage[] BuildPackages(SbomModel model, IReadOnlyDictionary<string, string> spdxIdsByKey)
    {
        var root = new SpdxPackage
        {
            SpdxId = RootPackageId,
            Name = model.Root.Name,
            DownloadLocation = "NOASSERTION",
            FilesAnalyzed = false
        };

        SpdxPackage[] packages = model.Components
            .Select(component => BuildPackage(component, spdxIdsByKey[component.Key]))
            .ToArray();

        return [root, .. packages];
    }

    /// <summary>
    /// Builds a single SPDX package entry, mapping <see cref="LicenseEvidence"/> onto SPDX's distinct
    /// <c>licenseDeclared</c>/<c>licenseConcluded</c> fields, and attaching an annotation summarizing any
    /// known OSV vulnerabilities.
    /// </summary>
    private static SpdxPackage BuildPackage(SbomComponent component, string spdxId)
    {
        (string declared, string concluded) = ResolveLicenseFields(component);

        return new SpdxPackage
        {
            SpdxId = spdxId,
            Name = component.Name,
            VersionInfo = component.Version,
            DownloadLocation = "NOASSERTION",
            FilesAnalyzed = false,
            LicenseDeclared = declared,
            LicenseConcluded = concluded,
            CopyrightText = "NOASSERTION",
            ExternalRefs =
            [
                new SpdxExternalRef
                {
                    ReferenceCategory = "PACKAGE-MANAGER",
                    ReferenceType = "purl",
                    ReferenceLocator = component.Purl
                }
            ],
            Annotations = BuildAnnotations(component)
        };
    }

    /// <summary>
    /// Maps a component's resolved license and its evidence onto SPDX's separate declared/concluded fields:
    /// a license that came from the package's own metadata is recorded as declared; one inferred from
    /// external evidence (e.g. a GitHub repository scan) is recorded as concluded instead.
    /// </summary>
    private static (string Declared, string Concluded) ResolveLicenseFields(SbomComponent component)
    {
        const string noAssertion = "NOASSERTION";

        if (string.IsNullOrWhiteSpace(component.License) ||
            component.License.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return (noAssertion, noAssertion);
        }

        return component.LicenseEvidence switch
        {
            LicenseEvidence.Concluded => (noAssertion, component.License),
            _ => (component.License, noAssertion)
        };
    }

    /// <summary>
    /// Builds a single annotation summarizing the OSV vulnerabilities known for a component, or
    /// <see langword="null"/> when none were recorded (i.e. <c>--report-risk</c> was not passed).
    /// </summary>
    private static SpdxAnnotation[]? BuildAnnotations(SbomComponent component)
    {
        if (component.Vulnerabilities.Count == 0)
        {
            return null;
        }

        string summary = string.Join(", ",
            component.Vulnerabilities.Select(v => $"{v.Id} (severity {v.Severity:0.0})"));

        return
        [
            new SpdxAnnotation
            {
                Annotator = "Tool: PackageGuard",
                AnnotationDate = FormatSpdxTimestamp(DateTimeOffset.UtcNow),
                AnnotationType = "OTHER",
                Comment = $"OSV vulnerabilities: {summary}"
            }
        ];
    }

    /// <summary>
    /// Builds the document's <c>DESCRIBES</c> relationship to the root package, the root's <c>DEPENDS_ON</c>
    /// relationships to every direct component, and every known parent-child edge for ecosystems with an
    /// accurate dependency graph.
    /// </summary>
    private static SpdxRelationship[] BuildRelationships(SbomModel model, IReadOnlyDictionary<string, string> spdxIdsByKey)
    {
        List<SpdxRelationship> relationships =
        [
            new()
            {
                SpdxElementId = "SPDXRef-DOCUMENT",
                RelationshipType = "DESCRIBES",
                RelatedSpdxElement = RootPackageId
            }
        ];

        relationships.AddRange(model.Components
            .Where(component => component.IsDirect)
            .Select(component => new SpdxRelationship
            {
                SpdxElementId = RootPackageId,
                RelationshipType = "DEPENDS_ON",
                RelatedSpdxElement = spdxIdsByKey[component.Key]
            }));

        relationships.AddRange(model.Edges
            .Where(edge => spdxIdsByKey.ContainsKey(edge.FromKey) && spdxIdsByKey.ContainsKey(edge.ToKey))
            .Select(edge => new SpdxRelationship
            {
                SpdxElementId = spdxIdsByKey[edge.FromKey],
                RelationshipType = "DEPENDS_ON",
                RelatedSpdxElement = spdxIdsByKey[edge.ToKey]
            }));

        return relationships.ToArray();
    }

    /// <summary>
    /// Builds a document-level comment explaining that one or more ecosystems have a flat (direct-only)
    /// dependency graph, or <see langword="null"/> when every ecosystem's graph is accurate.
    /// </summary>
    private static string? BuildComment(SbomModel model)
    {
        string[] inaccurateEcosystems = model.EcosystemGraphIsAccurate
            .Where(entry => !entry.Value)
            .Select(entry => entry.Key)
            .OrderBy(ecosystem => ecosystem, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return inaccurateEcosystems.Length > 0
            ? $"The dependency graph for the following ecosystems is flat (direct dependencies only) " +
              $"pending real parent-child parsing: {string.Join(", ", inaccurateEcosystems)}."
            : null;
    }

    /// <summary>
    /// Formats a timestamp per SPDX 2.3's required format: UTC, no fractional seconds, and a literal
    /// <c>Z</c> designator rather than a <c>+00:00</c> offset (e.g. <c>2026-08-17T19:12:46Z</c>).
    /// </summary>
    private static string FormatSpdxTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Shared JSON serializer options: null properties are omitted and output is indented.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    /// <summary>
    /// Root object of an SPDX 2.3 JSON document.
    /// </summary>
    private sealed class SpdxDocument
    {
        [JsonPropertyName("spdxVersion")]
        public string SpdxVersion { get; init; } = "SPDX-2.3";

        [JsonPropertyName("dataLicense")]
        public string DataLicense { get; init; } = "CC0-1.0";

        [JsonPropertyName("SPDXID")]
        public string SpdxId { get; init; } = "SPDXRef-DOCUMENT";

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("documentNamespace")]
        public string DocumentNamespace { get; init; } = "";

        [JsonPropertyName("creationInfo")]
        public SpdxCreationInfo CreationInfo { get; init; } = new();

        [JsonPropertyName("packages")]
        public SpdxPackage[] Packages { get; init; } = [];

        [JsonPropertyName("relationships")]
        public SpdxRelationship[] Relationships { get; init; } = [];

        [JsonPropertyName("comment")]
        public string? Comment { get; init; }
    }

    /// <summary>
    /// Records when and by which tool the SPDX document was created.
    /// </summary>
    private sealed class SpdxCreationInfo
    {
        [JsonPropertyName("created")]
        public string Created { get; init; } = "";

        [JsonPropertyName("creators")]
        public string[] Creators { get; init; } = [];
    }

    /// <summary>
    /// A single SPDX package: either the synthetic root or a resolved dependency.
    /// </summary>
    private sealed class SpdxPackage
    {
        [JsonPropertyName("SPDXID")]
        public string SpdxId { get; init; } = "";

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("versionInfo")]
        public string? VersionInfo { get; init; }

        [JsonPropertyName("downloadLocation")]
        public string DownloadLocation { get; init; } = "NOASSERTION";

        [JsonPropertyName("filesAnalyzed")]
        public bool FilesAnalyzed { get; init; }

        [JsonPropertyName("licenseDeclared")]
        public string? LicenseDeclared { get; init; }

        [JsonPropertyName("licenseConcluded")]
        public string? LicenseConcluded { get; init; }

        [JsonPropertyName("copyrightText")]
        public string? CopyrightText { get; init; }

        [JsonPropertyName("externalRefs")]
        public SpdxExternalRef[]? ExternalRefs { get; init; }

        [JsonPropertyName("annotations")]
        public SpdxAnnotation[]? Annotations { get; init; }
    }

    /// <summary>
    /// An external reference on a package, used here to carry the package's purl.
    /// </summary>
    private sealed class SpdxExternalRef
    {
        [JsonPropertyName("referenceCategory")]
        public string ReferenceCategory { get; init; } = "";

        [JsonPropertyName("referenceType")]
        public string ReferenceType { get; init; } = "";

        [JsonPropertyName("referenceLocator")]
        public string ReferenceLocator { get; init; } = "";
    }

    /// <summary>
    /// A freeform annotation attached to a package, used here to summarize known OSV vulnerabilities.
    /// </summary>
    private sealed class SpdxAnnotation
    {
        [JsonPropertyName("annotator")]
        public string Annotator { get; init; } = "";

        [JsonPropertyName("annotationDate")]
        public string AnnotationDate { get; init; } = "";

        [JsonPropertyName("annotationType")]
        public string AnnotationType { get; init; } = "OTHER";

        [JsonPropertyName("comment")]
        public string Comment { get; init; } = "";
    }

    /// <summary>
    /// A relationship between two SPDX elements, such as <c>DESCRIBES</c> or <c>DEPENDS_ON</c>.
    /// </summary>
    private sealed class SpdxRelationship
    {
        [JsonPropertyName("spdxElementId")]
        public string SpdxElementId { get; init; } = "";

        [JsonPropertyName("relationshipType")]
        public string RelationshipType { get; init; } = "";

        [JsonPropertyName("relatedSpdxElement")]
        public string RelatedSpdxElement { get; init; } = "";
    }
}
