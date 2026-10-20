using System.Text;
using System.Text.Json;

namespace PackageGuard.Core.GitHub;

/// <summary>
/// Names the JSON properties a caller reads from a GitHub response. Everything else is dropped before the response
/// is handed out or cached, so the persisted cache holds what PackageGuard uses instead of GitHub's full payload.
/// </summary>
/// <remarks>
/// A path is a dot-separated list of property names, such as <c>commit.author.date</c>. Arrays are walked
/// transparently, so the same path applies to a single object and to every element of a list of them.
/// </remarks>
internal sealed class GitHubResponseFields
{
    /// <summary>
    /// Keeps the response as GitHub returned it.
    /// </summary>
    public static readonly GitHubResponseFields All = new(null);

    /// <summary>
    /// The properties read from a repository resource. License fetching and risk enrichment both request it, so they
    /// share one selection and with that one cached response.
    /// </summary>
    public static readonly GitHubResponseFields Repository =
        Of("name", "html_url", "default_branch", "owner.login", "owner.type", "license.spdx_id");

    /// <summary>
    /// The properties to keep, keyed by name, each with the nested properties to keep below it. An empty node keeps
    /// the whole value. <see langword="null"/> keeps the entire response.
    /// </summary>
    private readonly Node? root;

    private GitHubResponseFields(string[]? paths)
    {
        if (paths is null)
        {
            Signature = "*";
            return;
        }

        root = new Node();
        foreach (string path in paths)
        {
            Node node = root;
            foreach (string name in path.Split('.'))
            {
                if (!node.Children.TryGetValue(name, out Node? child))
                {
                    child = new Node();
                    node.Children[name] = child;
                }

                node = child;
            }
        }

        Signature = string.Join(",", paths.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Identifies this selection, so that a body trimmed for one selection is never handed to a caller that needs
    /// another, for instance after an upgrade starts reading an extra property.
    /// </summary>
    public string Signature { get; }

    /// <summary>
    /// Keeps only the given property paths.
    /// </summary>
    public static GitHubResponseFields Of(params string[] paths) => new(paths);

    /// <summary>
    /// Returns <paramref name="body"/> without the properties this selection does not name. A body that is not valid
    /// JSON is returned unchanged, so the caller can report it.
    /// </summary>
    public string Trim(string body)
    {
        if (root is null)
        {
            return body;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                Write(document.RootElement, root, writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return body;
        }
    }

    /// <summary>
    /// Writes <paramref name="element"/>, keeping only the properties <paramref name="node"/> names.
    /// </summary>
    private static void Write(JsonElement element, Node node, Utf8JsonWriter writer)
    {
        if (node.Children.Count == 0)
        {
            element.WriteTo(writer);
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Write(item, node, writer);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (node.Children.TryGetValue(property.Name, out Node? child))
                    {
                        writer.WritePropertyName(property.Name);
                        Write(property.Value, child, writer);
                    }
                }

                writer.WriteEndObject();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    /// <summary>
    /// One property name in the selection, with the nested properties to keep below it.
    /// </summary>
    private sealed class Node
    {
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
    }
}
