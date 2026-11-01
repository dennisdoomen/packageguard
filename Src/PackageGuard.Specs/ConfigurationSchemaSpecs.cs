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

    // A private registry, because the global one refuses to register the same $id twice.
    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(File.ReadAllText(SchemaPath),
        new BuildOptions { SchemaRegistry = new SchemaRegistry() }));

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
    [DataRow("Schema/sample-config.json", DisplayName = "Samples/config.json")]
    [DataRow("Schema/repo-config.json", DisplayName = ".packageguard/config.json")]
    [DataRow("TestCases/SlnxApp/config.json", DisplayName = "SlnxApp test case")]
    public void An_existing_configuration_file_is_valid(string relativePath)
    {
        // Act
        EvaluationResults results = Evaluate(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, relativePath)));

        // Assert
        results.IsValid.Should().BeTrue(Describe(results));
    }

    [TestMethod]
    [DataRow(true, DisplayName = "With warnings and risk gates")]
    [DataRow(false, DisplayName = "Without warnings and risk gates")]
    public void The_configuration_generated_by_init_is_valid(bool includeWarningsAndRiskGates)
    {
        // Arrange
        string json = ConfigScaffolder.BuildConfigJson(SoftwareProfile.OpenSource, allowedLicenses: ["MIT", "LGPL-2.1-only"],
            warnLicenses: includeWarningsAndRiskGates ? ["LGPL-2.1-only"] : [], includeRiskGates: includeWarningsAndRiskGates);

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
    [DataRow("""{ "settings": { "vulnerabilityExceptions": [ { "vulnerability": "CVE-2024-0001", "reason": "  ", "reviewOn": "2027-01-01" } ] } }""",
        DisplayName = "Blank reason")]
    [DataRow("""{ "settings": { "deny": { "minPackageAgeDays": { "pypi": 3 } } } }""", DisplayName = "Unknown ecosystem")]
    public void Flags_an_invalid_configuration(string json)
    {
        // Act
        EvaluationResults results = Evaluate(json);

        // Assert
        results.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public void Accepts_the_ecosystem_names_in_any_casing()
    {
        // Act
        EvaluationResults results = Evaluate("""{ "settings": { "deny": { "minPackageAgeDays": { "NuGet": 3, "NPM": 14 } } } }""");

        // Assert
        results.IsValid.Should().BeTrue(Describe(results));
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
    /// Compares the public properties of <paramref name="type"/> (as camelCase JSON names) and their JSON types with
    /// the properties the schema object declares, recursing into nested settings objects and arrays of them.
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
            if (propertySchema is null)
            {
                continue;
            }

            string expectedType = GetJsonType(property.PropertyType);
            string actualType = propertySchema["type"]?.GetValue<string>();
            if (actualType != expectedType)
            {
                yield return $"{path}.{name} has type {actualType} in the schema, but {property.PropertyType.Name} needs {expectedType}";
            }

            Type propertyType = property.PropertyType.IsArray ? property.PropertyType.GetElementType()! : property.PropertyType;
            if (!IsSettingsObject(propertyType))
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

    private static string GetJsonType(Type type)
    {
        Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType switch
        {
            _ when underlyingType == typeof(bool) => "boolean",
            _ when underlyingType == typeof(int) => "integer",
            _ when underlyingType == typeof(double) => "number",
            _ when underlyingType == typeof(string) || underlyingType == typeof(DateOnly) => "string",
            _ when underlyingType.IsArray => "array",
            _ => "object"
        };
    }

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
