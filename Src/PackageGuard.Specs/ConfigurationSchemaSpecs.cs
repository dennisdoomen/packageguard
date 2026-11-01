using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.Schema;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.Scaffolding;

namespace PackageGuard.Specs;

/// <summary>
/// Guards the published JSON schema of the configuration file (<c>website/static/schemas/config.json</c>) against
/// drifting apart from <see cref="PolicySettings"/>, the sample configuration and what <c>init</c> generates.
/// </summary>
[TestClass]
public class ConfigurationSchemaSpecs
{
    private static readonly string SchemaPath = Path.Combine(AppContext.BaseDirectory, "Schema", "config.schema.json");
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "Schema", "sample-config.json");

    // The schema registers itself globally by its $id, so it can only be loaded once.
    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(File.ReadAllText(SchemaPath)));

    private static readonly JsonDocumentOptions JsonWithComments = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [TestMethod]
    public void The_schema_describes_every_property_of_the_policy_settings()
    {
        // Arrange
        JsonNode settingsSchema = LoadSchemaNode()["properties"]!["settings"]!;

        // Act
        List<string> differences = FindDifferences(typeof(PolicySettings), settingsSchema, "settings").ToList();

        // Assert
        differences.Should().BeEmpty();
    }

    [TestMethod]
    public void Every_property_in_the_schema_has_a_description()
    {
        // Arrange
        JsonNode schema = LoadSchemaNode();

        // Act
        List<string> undocumented = FindPropertiesWithoutDescription(schema, "").ToList();

        // Assert
        undocumented.Should().BeEmpty();
    }

    [TestMethod]
    public void The_sample_configuration_is_valid()
    {
        // Act
        EvaluationResults results = Evaluate(File.ReadAllText(SamplePath));

        // Assert
        results.IsValid.Should().BeTrue(Describe(results));
    }

    [TestMethod]
    public void The_configuration_generated_by_init_is_valid()
    {
        // Arrange
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.OpenSource, allowedLicenses: ["MIT", "LGPL-2.1-only"],
            warnLicenses: ["LGPL-2.1-only"], includeRiskGates: true);

        // Act
        EvaluationResults results = Evaluate(json);

        // Assert
        results.IsValid.Should().BeTrue(Describe(results));
    }

    [TestMethod]
    public void The_configuration_generated_by_init_refers_to_the_schema()
    {
        // Act
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.Proprietary, allowedLicenses: ["MIT"],
            warnLicenses: [], includeRiskGates: false);

        // Assert
        JsonNode.Parse(json, documentOptions: JsonWithComments)!["$schema"]!.GetValue<string>()
            .Should().Be(LoadSchemaNode()["$id"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow("""{ "settings": { "deny": { "maxOveralRisk": 60 } } }""", DisplayName = "Misspelled property")]
    [DataRow("""{ "settings": { "deny": { "maxOverallRisk": 101 } } }""", DisplayName = "Risk score above 100")]
    [DataRow("""{ "settings": { "deny": { "maxSecurityRisk": -1 } } }""", DisplayName = "Negative risk score")]
    [DataRow("""{ "settings": { "riskExceptions": [ { "package": "x", "expiresOn": "next year" } ] } }""",
        DisplayName = "Invalid date")]
    [DataRow("""{ "settings": { "vulnerabilityExceptions": [ { "vulnerability": "CVE-2024-0001" } ] } }""",
        DisplayName = "Missing reason and review date")]
    public void Flags_an_invalid_configuration(string json)
    {
        // Act
        EvaluationResults results = Evaluate(json);

        // Assert
        results.IsValid.Should().BeFalse();
    }

    private static JsonNode LoadSchemaNode() => JsonNode.Parse(File.ReadAllText(SchemaPath))!;

    private static EvaluationResults Evaluate(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, JsonWithComments);

        return Schema.Value.Evaluate(document.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        });
    }

    private static string Describe(EvaluationResults results)
    {
        IEnumerable<string> errors = (results.Details ?? [])
            .Where(detail => detail.Errors is not null)
            .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}"));

        return string.Join(Environment.NewLine, errors);
    }

    /// <summary>
    /// Compares the public properties of <paramref name="type"/> (as camelCase JSON names) with the properties the
    /// schema object declares, recursing into nested settings objects and arrays of them.
    /// </summary>
    private static IEnumerable<string> FindDifferences(Type type, JsonNode schema, string path)
    {
        JsonObject schemaProperties = schema["properties"]!.AsObject();
        Dictionary<string, PropertyInfo> typeProperties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name));

        foreach (string missing in typeProperties.Keys.Except(schemaProperties.Select(property => property.Key)))
        {
            yield return $"{path}.{missing} is missing from the schema";
        }

        foreach (string unknown in schemaProperties.Select(property => property.Key).Except(typeProperties.Keys))
        {
            yield return $"{path}.{unknown} is in the schema but not in {type.Name}";
        }

        foreach ((string name, PropertyInfo property) in typeProperties)
        {
            JsonNode propertySchema = schemaProperties[name];
            Type propertyType = property.PropertyType.IsArray ? property.PropertyType.GetElementType()! : property.PropertyType;
            if (propertySchema is null || !IsSettingsObject(propertyType))
            {
                continue;
            }

            JsonNode objectSchema = property.PropertyType.IsArray ? propertySchema["items"]! : propertySchema;
            foreach (string difference in FindDifferences(propertyType, objectSchema, $"{path}.{name}"))
            {
                yield return difference;
            }
        }
    }

    private static bool IsSettingsObject(Type type) => type.DeclaringType == typeof(PolicySettings);

    private static IEnumerable<string> FindPropertiesWithoutDescription(JsonNode schema, string path)
    {
        if (schema["properties"] is JsonObject properties)
        {
            foreach ((string name, JsonNode property) in properties)
            {
                if (property!["description"] is null)
                {
                    yield return $"{path}.{name}";
                }

                foreach (string nested in FindPropertiesWithoutDescription(property, $"{path}.{name}"))
                {
                    yield return nested;
                }
            }
        }

        if (schema["items"] is JsonObject items)
        {
            foreach (string nested in FindPropertiesWithoutDescription(items, $"{path}[]"))
            {
                yield return nested;
            }
        }
    }
}
