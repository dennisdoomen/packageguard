using PackageGuard.Core.Package;

namespace PackageGuard.Core.Sbom;

/// <summary>
/// Builds and writes a Software Bill of Materials for a set of resolved packages, choosing the CycloneDX
/// or SPDX format writer based on a case-insensitive format name.
/// </summary>
internal static class SbomWriter
{
    /// <summary>
    /// Builds the SBOM for <paramref name="packages"/> in the requested <paramref name="format"/>
    /// ("cyclonedx" or "spdx", case-insensitive; unrecognized values fall back to CycloneDX) and writes it
    /// to <paramref name="outputPath"/>, creating any missing parent directories.
    /// </summary>
    public static void Write(IReadOnlyCollection<PackageInfo> packages, string projectPath, string format, string outputPath)
    {
        SbomModel model = SbomModelBuilder.Build(packages, projectPath);

        string json = format.Equals("spdx", StringComparison.OrdinalIgnoreCase)
            ? SpdxSbomWriter.Build(model)
            : CycloneDxSbomWriter.Build(model);

        string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        File.WriteAllText(outputPath, json);
    }
}
