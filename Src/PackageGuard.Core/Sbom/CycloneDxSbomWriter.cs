using System.Text.Json;
using System.Text.Json.Serialization;
using PackageGuard.Core.Package;

namespace PackageGuard.Core.Sbom;

/// <summary>
/// Builds a CycloneDX 1.6 JSON Software Bill of Materials document from a shared <see cref="SbomModel"/>.
/// </summary>
internal static class CycloneDxSbomWriter
{
    /// <summary>
    /// Builds the CycloneDX JSON document for <paramref name="model"/>.
    /// </summary>
    public static string Build(SbomModel model)
    {
        var bom = new CycloneDxBom
        {
            Metadata = BuildMetadata(model),
            Components = model.Components.Select(BuildComponent).ToArray(),
            Dependencies = BuildDependencies(model),
            Vulnerabilities = BuildVulnerabilities(model)
        };

        return JsonSerializer.Serialize(bom, SerializerOptions);
    }

    /// <summary>
    /// Builds the document metadata, including the synthetic root component and, when one or more
    /// ecosystems lack an accurate dependency graph, a caveat property explaining the limitation.
    /// </summary>
    private static CycloneDxMetadata BuildMetadata(SbomModel model)
    {
        var metadata = new CycloneDxMetadata
        {
            Timestamp = model.GeneratedAt,
            Tools = new CycloneDxToolsChoice
            {
                Components =
                [
                    new CycloneDxToolComponent
                    {
                        Type = "application",
                        Name = "PackageGuard",
                        Manufacturer = new CycloneDxOrganizationalEntity { Name = "Dennis Doomen" }
                    }
                ]
            },
            Component = new CycloneDxComponent
            {
                Type = "application",
                BomRef = model.Root.BomRef,
                Name = model.Root.Name
            }
        };

        string[] inaccurateEcosystems = model.EcosystemGraphIsAccurate
            .Where(entry => !entry.Value)
            .Select(entry => entry.Key)
            .OrderBy(ecosystem => ecosystem, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (inaccurateEcosystems.Length > 0)
        {
            metadata.Properties =
            [
                new CycloneDxProperty
                {
                    Name = "packageguard:flat-dependency-graph",
                    Value =
                        $"The dependency graph for the following ecosystems is flat (direct dependencies only) " +
                        $"pending real parent-child parsing: {string.Join(", ", inaccurateEcosystems)}."
                }
            ];
        }

        return metadata;
    }

    /// <summary>
    /// Builds a single CycloneDX component entry, including its purl, scope, licenses, and project properties.
    /// </summary>
    private static CycloneDxComponent BuildComponent(SbomComponent component)
    {
        return new CycloneDxComponent
        {
            Type = "library",
            BomRef = component.Purl,
            Name = component.Name,
            Version = component.Version,
            Purl = component.Purl,
            Scope = component.IsDirect ? "required" : null,
            Licenses = BuildLicenses(component),
            ExternalReferences = BuildExternalReferences(component),
            Properties = BuildComponentProperties(component)
        };
    }

    /// <summary>
    /// Builds the <c>licenses</c> array for a component, recording whether the license was declared by the
    /// package's own metadata or concluded from external evidence.
    /// </summary>
    private static CycloneDxLicenseEntry[]? BuildLicenses(SbomComponent component)
    {
        if (string.IsNullOrWhiteSpace(component.License) ||
            component.License.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return
        [
            new CycloneDxLicenseEntry
            {
                License = new CycloneDxLicense
                {
                    Id = component.License,
                    Acknowledgement = component.LicenseEvidence switch
                    {
                        LicenseEvidence.Concluded => "concluded",
                        LicenseEvidence.Declared => "declared",
                        _ => null
                    }
                }
            }
        ];
    }

    /// <summary>
    /// Builds the <c>externalReferences</c> array from the component's repository and license URLs.
    /// </summary>
    private static CycloneDxExternalReference[]? BuildExternalReferences(SbomComponent component)
    {
        List<CycloneDxExternalReference> references = [];

        if (!string.IsNullOrWhiteSpace(component.RepositoryUrl))
        {
            references.Add(new CycloneDxExternalReference { Type = "vcs", Url = component.RepositoryUrl });
        }

        if (!string.IsNullOrWhiteSpace(component.LicenseUrl))
        {
            references.Add(new CycloneDxExternalReference { Type = "license", Url = component.LicenseUrl });
        }

        return references.Count > 0 ? references.ToArray() : null;
    }

    /// <summary>
    /// Builds one <c>packageguard:project</c> property per consuming project, for traceability.
    /// </summary>
    private static CycloneDxProperty[]? BuildComponentProperties(SbomComponent component)
    {
        if (component.Projects.Count == 0)
        {
            return null;
        }

        return component.Projects
            .Select(project => new CycloneDxProperty { Name = "packageguard:project", Value = project })
            .ToArray();
    }

    /// <summary>
    /// Builds the <c>dependencies</c> graph: the root's direct dependencies, plus every known
    /// parent-child edge for ecosystems with an accurate dependency graph.
    /// </summary>
    private static CycloneDxDependency[] BuildDependencies(SbomModel model)
    {
        List<CycloneDxDependency> dependencies = [];

        string[] directRefs = model.Components
            .Where(component => component.IsDirect)
            .Select(component => component.Purl)
            .ToArray();

        dependencies.Add(new CycloneDxDependency { Ref = model.Root.BomRef, DependsOn = directRefs });

        Dictionary<string, string> purlByKey = model.Components.ToDictionary(c => c.Key, c => c.Purl, StringComparer.OrdinalIgnoreCase);

        foreach (var group in model.Edges.GroupBy(edge => edge.FromKey, StringComparer.OrdinalIgnoreCase))
        {
            if (!purlByKey.TryGetValue(group.Key, out string? fromPurl))
            {
                continue;
            }

            string[] dependsOn = group
                .Select(edge => purlByKey.GetValueOrDefault(edge.ToKey))
                .Where(purl => purl is not null)
                .Select(purl => purl!)
                .ToArray();

            dependencies.Add(new CycloneDxDependency { Ref = fromPurl, DependsOn = dependsOn });
        }

        return dependencies.ToArray();
    }

    /// <summary>
    /// Builds the <c>vulnerabilities</c> section from every component's OSV vulnerability records, or
    /// <see langword="null"/> when none carry any (i.e. <c>--report-risk</c> was not passed).
    /// </summary>
    private static CycloneDxVulnerability[]? BuildVulnerabilities(SbomModel model)
    {
        var vulnerabilities = model.Components
            .SelectMany(component => component.Vulnerabilities.Select(vulnerability => (component, vulnerability)))
            .GroupBy(entry => entry.vulnerability.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CycloneDxVulnerability
            {
                Id = group.Key,
                Ratings = [new CycloneDxVulnerabilityRating { Score = group.Max(entry => entry.vulnerability.Severity), Method = "other" }],
                Advisories = group.SelectMany(entry => entry.vulnerability.References)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(url => new CycloneDxAdvisory { Url = url })
                    .ToArray(),
                Affects = group.Select(entry => new CycloneDxAffects { Ref = entry.component.Purl })
                    .DistinctBy(affects => affects.Ref, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .ToArray();

        return vulnerabilities.Length > 0 ? vulnerabilities : null;
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
    /// Root object of a CycloneDX 1.6 JSON BOM document.
    /// </summary>
    private sealed class CycloneDxBom
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; init; } = "http://cyclonedx.org/schema/bom-1.6.schema.json";

        [JsonPropertyName("bomFormat")]
        public string BomFormat { get; init; } = "CycloneDX";

        [JsonPropertyName("specVersion")]
        public string SpecVersion { get; init; } = "1.6";

        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("metadata")]
        public CycloneDxMetadata Metadata { get; init; } = new();

        [JsonPropertyName("components")]
        public CycloneDxComponent[] Components { get; init; } = [];

        [JsonPropertyName("dependencies")]
        public CycloneDxDependency[] Dependencies { get; init; } = [];

        [JsonPropertyName("vulnerabilities")]
        public CycloneDxVulnerability[]? Vulnerabilities { get; init; }
    }

    /// <summary>
    /// Document-level metadata: generation timestamp, generating tool, and the synthetic root component.
    /// </summary>
    private sealed class CycloneDxMetadata
    {
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; init; }

        [JsonPropertyName("tools")]
        public CycloneDxToolsChoice Tools { get; init; } = new();

        [JsonPropertyName("component")]
        public CycloneDxComponent Component { get; init; } = new();

        [JsonPropertyName("properties")]
        public CycloneDxProperty[]? Properties { get; set; }
    }

    /// <summary>
    /// CycloneDX 1.6's <c>tools</c> choice wrapper, using the modern <c>components</c> array form.
    /// </summary>
    private sealed class CycloneDxToolsChoice
    {
        [JsonPropertyName("components")]
        public CycloneDxToolComponent[] Components { get; init; } = [];
    }

    /// <summary>
    /// Describes the tool that generated this SBOM.
    /// </summary>
    private sealed class CycloneDxToolComponent
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "application";

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("manufacturer")]
        public CycloneDxOrganizationalEntity? Manufacturer { get; init; }
    }

    /// <summary>
    /// A CycloneDX organizational entity, used here to name the manufacturer of the generating tool.
    /// </summary>
    private sealed class CycloneDxOrganizationalEntity
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";
    }

    /// <summary>
    /// A single CycloneDX component: either a package or, for the root, the analyzed solution itself.
    /// </summary>
    private sealed class CycloneDxComponent
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "library";

        [JsonPropertyName("bom-ref")]
        public string BomRef { get; init; } = "";

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("purl")]
        public string? Purl { get; init; }

        [JsonPropertyName("scope")]
        public string? Scope { get; init; }

        [JsonPropertyName("licenses")]
        public CycloneDxLicenseEntry[]? Licenses { get; init; }

        [JsonPropertyName("externalReferences")]
        public CycloneDxExternalReference[]? ExternalReferences { get; init; }

        [JsonPropertyName("properties")]
        public CycloneDxProperty[]? Properties { get; init; }
    }

    /// <summary>
    /// Wraps a single license entry within a component's <c>licenses</c> array.
    /// </summary>
    private sealed class CycloneDxLicenseEntry
    {
        [JsonPropertyName("license")]
        public CycloneDxLicense License { get; init; } = new();
    }

    /// <summary>
    /// A CycloneDX license identifier, optionally annotated with whether it was declared or concluded.
    /// </summary>
    private sealed class CycloneDxLicense
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("acknowledgement")]
        public string? Acknowledgement { get; init; }
    }

    /// <summary>
    /// An external reference (repository, license text) associated with a component.
    /// </summary>
    private sealed class CycloneDxExternalReference
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "";

        [JsonPropertyName("url")]
        public string Url { get; init; } = "";
    }

    /// <summary>
    /// A free-form name/value property attached to a component or the document metadata.
    /// </summary>
    private sealed class CycloneDxProperty
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("value")]
        public string Value { get; init; } = "";
    }

    /// <summary>
    /// A single entry in the CycloneDX <c>dependencies</c> graph: a component and its direct dependencies.
    /// </summary>
    private sealed class CycloneDxDependency
    {
        [JsonPropertyName("ref")]
        public string Ref { get; init; } = "";

        [JsonPropertyName("dependsOn")]
        public string[] DependsOn { get; init; } = [];
    }

    /// <summary>
    /// A single vulnerability affecting one or more components, sourced from OSV data.
    /// </summary>
    private sealed class CycloneDxVulnerability
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("ratings")]
        public CycloneDxVulnerabilityRating[] Ratings { get; init; } = [];

        [JsonPropertyName("advisories")]
        public CycloneDxAdvisory[]? Advisories { get; init; }

        [JsonPropertyName("affects")]
        public CycloneDxAffects[] Affects { get; init; } = [];
    }

    /// <summary>
    /// A severity rating for a vulnerability.
    /// </summary>
    private sealed class CycloneDxVulnerabilityRating
    {
        [JsonPropertyName("score")]
        public double Score { get; init; }

        [JsonPropertyName("method")]
        public string Method { get; init; } = "other";
    }

    /// <summary>
    /// A reference URL for a vulnerability, such as a security advisory.
    /// </summary>
    private sealed class CycloneDxAdvisory
    {
        [JsonPropertyName("url")]
        public string Url { get; init; } = "";
    }

    /// <summary>
    /// Identifies a component affected by a vulnerability.
    /// </summary>
    private sealed class CycloneDxAffects
    {
        [JsonPropertyName("ref")]
        public string Ref { get; init; } = "";
    }
}
