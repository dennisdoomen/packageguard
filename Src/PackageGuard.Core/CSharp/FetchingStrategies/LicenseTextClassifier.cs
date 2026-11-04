using System.Text.RegularExpressions;

namespace PackageGuard.Core.CSharp.FetchingStrategies;

/// <summary>
/// Identifies a well-known license from its text. It only concludes a license when the identifying title is in the header
/// of the text and the text does not describe a dual or conditional license.
/// </summary>
internal static partial class LicenseTextClassifier
{
    private const int HeaderLineCount = 15;

    private static readonly string[] DualLicenseMarkers =
    [
        "split licensed",
        "split license",
        "dual licensed",
        "dual-licensed",
        "commercial use license",
        "commercial license"
    ];

    /// <summary>
    /// Returns the SPDX identifier (or well-known name) of the license, or <c>null</c> if the text does not clearly
    /// contain exactly one well-known license.
    /// </summary>
    public static string? Identify(string licenseText)
    {
        string text = Normalize(licenseText);

        if (DualLicenseMarkers.Any(text.Contains))
        {
            return null;
        }

        string header = GetHeader(licenseText);

        string?[] candidates =
        [
            IdentifyMit(header),
            IdentifyApache(header),
            IdentifyGnu(header),
            IdentifyMicrosoft(header)
        ];

        string[] matches = candidates.OfType<string>().ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string? IdentifyMit(string header) =>
        header.Contains("mit license") || header.Contains("permission is hereby granted, free of charge")
            ? "MIT"
            : null;

    private static string? IdentifyApache(string header) =>
        header.Contains("apache license") && header.Contains("version 2.0") ? "Apache-2.0" : null;

    private static string? IdentifyMicrosoft(string header) =>
        header.Contains("microsoft software license terms") ? "Microsoft .NET Library License" : null;

    private static string? IdentifyGnu(string header)
    {
        if (header.Contains("gnu affero general public license"))
        {
            return "AGPL-3.0";
        }

        if (header.Contains("gnu lesser general public license") || header.Contains("gnu library general public license"))
        {
            return header.Contains("version 2.1") ? "LGPL-2.1" : header.Contains("version 3") ? "LGPL-3.0" : null;
        }

        if (header.Contains("gnu general public license"))
        {
            return header.Contains("version 2") ? "GPL-2.0" : header.Contains("version 3") ? "GPL-3.0" : null;
        }

        return null;
    }

    private static string GetHeader(string licenseText)
    {
        IEnumerable<string> lines = licenseText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Take(HeaderLineCount);

        return Normalize(string.Join(' ', lines));
    }

    private static string Normalize(string text) => Whitespace().Replace(text, " ").Trim().ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
