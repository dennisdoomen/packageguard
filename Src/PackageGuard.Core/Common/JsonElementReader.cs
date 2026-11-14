using System.Text.Json;

namespace PackageGuard.Core.Common;

/// <summary>Tolerant readers for optional properties of JSON responses from external APIs.</summary>
internal static class JsonElementReader
{
    /// <summary>
    /// Reads and parses the date-valued property identified by <paramref name="propertyName"/> from <paramref name="element"/>;
    /// returns <see langword="null"/> if the property is absent or unparseable.
    /// </summary>
    public static DateTimeOffset? TryReadDate(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               DateTimeOffset.TryParse(property.GetString(), out DateTimeOffset value)
            ? value
            : null;
    }

    /// <summary>
    /// Reads the string-valued property identified by <paramref name="propertyName"/> from <paramref name="element"/>;
    /// returns <see langword="null"/> if the property is absent or not a string.
    /// </summary>
    public static string? TryReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
