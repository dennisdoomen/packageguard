namespace PackageGuard.Core.Sbom;

/// <summary>
/// Builds Package URL (purl) identifiers for packages, following the
/// <see href="https://github.com/package-url/purl-spec">package-url/purl-spec</see>.
/// </summary>
internal static class PackageUrlBuilder
{
    /// <summary>
    /// Builds the purl for a package with the given ecosystem, name, and version.
    /// </summary>
    public static string Build(string ecosystem, string name, string version)
    {
        string type = ToPurlType(ecosystem);
        string encodedVersion = Uri.EscapeDataString(version);

        return $"pkg:{type}/{BuildNamespaceAndName(type, name)}@{encodedVersion}";
    }

    /// <summary>
    /// Maps a PackageGuard ecosystem identifier ("nuget"/"npm") to its purl type.
    /// </summary>
    private static string ToPurlType(string ecosystem) => ecosystem switch
    {
        "npm" => "npm",
        _ => "nuget"
    };

    /// <summary>
    /// Builds the purl namespace/name segment, percent-encoding npm scoped package names
    /// (e.g. <c>@scope/name</c> becomes <c>%40scope/name</c>) per the purl spec.
    /// </summary>
    private static string BuildNamespaceAndName(string type, string name)
    {
        if (type == "npm" && name.StartsWith('@'))
        {
            int slashIndex = name.IndexOf('/');
            if (slashIndex > 0)
            {
                string scope = name[..slashIndex];
                string packageName = name[(slashIndex + 1)..];
                return $"{Uri.EscapeDataString(scope)}/{Uri.EscapeDataString(packageName)}";
            }
        }

        return Uri.EscapeDataString(name);
    }
}
