using System.Net;
using System.Text.RegularExpressions;

namespace PackageGuard.Core.CSharp.FetchingStrategies;

/// <summary>
/// Identifies a well-known license from its text. It only concludes a license when the identifying title is in the header
/// of the text and the text does not describe a dual or conditional license.
/// </summary>
internal static partial class LicenseTextClassifier
{
    private const int HeaderLineCount = 15;
    private const int TitleLineCount = 3;

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
        licenseText = ExtractLicenseText(licenseText);

        if (DualLicenseMarkers.Any(Normalize(licenseText).Contains))
        {
            return null;
        }

        string header = GetHeader(licenseText, HeaderLineCount);
        string title = GetHeader(licenseText, TitleLineCount);

        string?[] candidates = [IdentifyMit(header), IdentifyApache(header), IdentifyGnu(title), IdentifyMicrosoft(header)];

        string[] matches = candidates.OfType<string>().ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    // Only the title identifies MIT. The "Permission is hereby granted, free of charge" sentence also opens licenses such as
    // JSON, BSL-1.0, X11, ICU and Unicode, which are not MIT.
    private static string? IdentifyMit(string header) => header.Contains("mit license") ? "MIT" : null;

    private static string? IdentifyApache(string header) =>
        ApacheTwoPointZero().IsMatch(header) ? "Apache-2.0" : null;

    private static string? IdentifyMicrosoft(string header) =>
        header.Contains("microsoft software license terms") ? "Microsoft .NET Library License" : null;

    // The GNU licenses are identified by the title only, since their preambles refer to each other.
    private static string? IdentifyGnu(string title)
    {
        if (title.Contains("gnu affero general public license"))
        {
            return "AGPL-3.0";
        }

        if (title.Contains("gnu lesser general public license") || title.Contains("gnu library general public license"))
        {
            return title.Contains("version 2.1") ? "LGPL-2.1" : title.Contains("version 3") ? "LGPL-3.0" : null;
        }

        if (title.Contains("gnu general public license"))
        {
            return title.Contains("version 2") ? "GPL-2.0" : title.Contains("version 3") ? "GPL-3.0" : null;
        }

        return null;
    }

    /// <summary>
    /// Strips the HTML around the license text, for instance on the NuGet.org license page, so that only the license remains.
    /// </summary>
    private static string ExtractLicenseText(string content)
    {
        content = content.TrimStart('﻿');
        if (!content.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        Match licenseFile = LicenseFileContents().Match(content);
        string text = licenseFile.Success ? licenseFile.Groups[1].Value : GetPageTitle(content) + "\n" + GetPageBody(content);

        return WebUtility.HtmlDecode(HtmlTag().Replace(text, " ")).Trim('﻿', ' ', '\r', '\n');
    }

    private static string GetPageTitle(string html)
    {
        Match title = PageTitle().Match(html);
        return title.Success ? title.Groups[1].Value : string.Empty;
    }

    private static string GetPageBody(string html) =>
        BlockBreak().Replace(HtmlNoise().Replace(html, " "), "\n");

    private static string GetHeader(string licenseText, int lineCount)
    {
        IEnumerable<string> lines = licenseText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Take(lineCount);

        return Normalize(string.Join(' ', lines));
    }

    private static string Normalize(string text) => Whitespace().Replace(text, " ").Trim().ToLowerInvariant();

    [GeneratedRegex(@"apache license,? (version )?2\.0")]
    private static partial Regex ApacheTwoPointZero();

    [GeneratedRegex("<pre[^>]*license-file-contents[^>]*>(.*?)</pre>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LicenseFileContents();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PageTitle();

    [GeneratedRegex(@"<(script|style|head)\b.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlNoise();

    [GeneratedRegex(@"<(br|/?(p|div|h[1-6]|li|ul|ol|tr|table|pre|section|article|nav|header|footer))\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreak();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
