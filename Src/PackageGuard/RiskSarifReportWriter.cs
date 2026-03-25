using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using PackageGuard.Core;

namespace PackageGuard;

internal static class RiskSarifReportWriter
{
    private const string MediumRiskRuleId = "packageguard/risk-medium";
    private const string HighRiskRuleId = "packageguard/risk-high";

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
                    $"Operational {package.RiskDimensions.OperationalRisk.ToString("0.0", CultureInfo.InvariantCulture)}/10."
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

    private static string ResolveLocationPath(string projectPath)
    {
        if (File.Exists(projectPath))
        {
            return projectPath;
        }

        if (!Directory.Exists(projectPath))
        {
            return projectPath;
        }

        string[] preferredFiles =
        [
            Path.Combine(projectPath, ".packageguard", "config.json"),
            Path.Combine(projectPath, "packageguard.config.json")
        ];

        string? preferred = preferredFiles.FirstOrDefault(File.Exists);
        if (preferred is not null)
        {
            return preferred;
        }

        string? projectFile = Directory.EnumerateFiles(projectPath, "*.sln").FirstOrDefault()
            ?? Directory.EnumerateFiles(projectPath, "*.slnx").FirstOrDefault()
            ?? Directory.EnumerateFiles(projectPath, "*.csproj").FirstOrDefault()
            ?? Directory.EnumerateFiles(projectPath, "package.json").FirstOrDefault();

        return projectFile ?? projectPath;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private sealed class SarifLog
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; init; } = "https://json.schemastore.org/sarif-2.1.0.json";

        [JsonPropertyName("version")]
        public string Version { get; init; } = "2.1.0";

        [JsonPropertyName("runs")]
        public SarifRun[] Runs { get; init; } = [];
    }

    private sealed class SarifRun
    {
        [JsonPropertyName("tool")]
        public SarifTool Tool { get; init; } = new();

        [JsonPropertyName("results")]
        public SarifResult[] Results { get; init; } = [];
    }

    private sealed class SarifTool
    {
        [JsonPropertyName("driver")]
        public SarifDriver Driver { get; init; } = new();
    }

    private sealed class SarifDriver
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("informationUri")]
        public string? InformationUri { get; init; }

        [JsonPropertyName("rules")]
        public SarifReportingDescriptor[] Rules { get; init; } = [];
    }

    private sealed class SarifReportingDescriptor
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("shortDescription")]
        public SarifMessage ShortDescription { get; init; } = new();

        [JsonPropertyName("fullDescription")]
        public SarifMessage FullDescription { get; init; } = new();

        [JsonPropertyName("helpUri")]
        public string? HelpUri { get; init; }

        [JsonPropertyName("properties")]
        public Dictionary<string, string>? Properties { get; init; }
    }

    private sealed class SarifResult
    {
        [JsonPropertyName("ruleId")]
        public string RuleId { get; init; } = "";

        [JsonPropertyName("level")]
        public string Level { get; init; } = "";

        [JsonPropertyName("message")]
        public SarifMessage Message { get; init; } = new();

        [JsonPropertyName("partialFingerprints")]
        public Dictionary<string, string>? PartialFingerprints { get; init; }

        [JsonPropertyName("properties")]
        public Dictionary<string, object>? Properties { get; init; }

        [JsonPropertyName("locations")]
        public SarifLocation[] Locations { get; init; } = [];
    }

    private sealed class SarifMessage
    {
        [JsonPropertyName("text")]
        public string Text { get; init; } = "";
    }

    private sealed class SarifLocation
    {
        [JsonPropertyName("physicalLocation")]
        public SarifPhysicalLocation PhysicalLocation { get; init; } = new();
    }

    private sealed class SarifPhysicalLocation
    {
        [JsonPropertyName("artifactLocation")]
        public SarifArtifactLocation ArtifactLocation { get; init; } = new();

        [JsonPropertyName("region")]
        public SarifRegion Region { get; init; } = new();
    }

    private sealed class SarifArtifactLocation
    {
        [JsonPropertyName("uri")]
        public string Uri { get; init; } = "";
    }

    private sealed class SarifRegion
    {
        [JsonPropertyName("startLine")]
        public int StartLine { get; init; }
    }
}
