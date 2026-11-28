using System.Xml.Linq;
using NuGet.Configuration;
using NuGet.Packaging;

namespace PackageGuard.Core.CSharp;

/// <summary>
/// Detects effective NuGet source mappings that allow nuget.org and another source to supply the same package.
/// </summary>
internal sealed class NuGetSourceMappingChecker
{
    private const int MaxPackageIdLength = 100;

    /// <summary>
    /// Checks the NuGet settings that apply to a project directory without contacting any package source.
    /// </summary>
    public IReadOnlyCollection<NuGetSourceWarning> Check(string projectDirectory)
    {
        ISettings settings = Settings.LoadDefaultSettings(projectDirectory);
        PackageSource[] sources = new PackageSourceProvider(settings).LoadPackageSources().Where(source => source.IsEnabled).ToArray();
        PackageSource[] publicSources = sources.Where(IsNuGetOrg).ToArray();
        PackageSource[] otherSources = sources.Where(source => !IsNuGetOrg(source)).ToArray();
        PackageSourceMapping mapping = PackageSourceMapping.GetPackageSourceMapping(settings);
        var mappings = settings.GetSection("packageSourceMapping")?.Items.OfType<PackageSourceMappingSourceItem>().ToArray() ?? [];
        var warnings = new List<NuGetSourceWarning>();

        foreach (PackageSource publicSource in publicSources)
        {
            foreach (PackageSource otherSource in otherSources)
            {
                AddWarnings(warnings, settings, mapping, mappings, publicSource, otherSource);
            }
        }

        return warnings;
    }

    private static void AddWarnings(List<NuGetSourceWarning> warnings, ISettings settings, PackageSourceMapping mapping,
        PackageSourceMappingSourceItem[] mappings, PackageSource publicSource, PackageSource otherSource)
    {
        string publicFile = FindSourceFile(settings, publicSource);
        string otherFile = FindSourceFile(settings, otherSource);

        if (!mapping.IsEnabled)
        {
            string mappingFiles = string.Join(", ", settings.GetConfigFilePaths()
                .Where(path => XDocument.Load(path).Root?.Element("packageSourceMapping") is not null));
            warnings.Add(new NuGetSourceWarning(publicSource.Name, otherSource.Name, publicFile, otherFile, "", mappingFiles));
            return;
        }

        PackageSourceMappingSourceItem? publicMapping = mappings.FirstOrDefault(item =>
            item.Key.Equals(publicSource.Name, StringComparison.OrdinalIgnoreCase));
        PackageSourceMappingSourceItem? otherMapping = mappings.FirstOrDefault(item =>
            item.Key.Equals(otherSource.Name, StringComparison.OrdinalIgnoreCase));

        if (publicMapping is null || otherMapping is null)
        {
            return;
        }

        foreach (PackagePatternItem pattern in publicMapping.Patterns)
        {
            PackagePatternItem? otherPattern = otherMapping.Patterns.FirstOrDefault(item =>
                item.Pattern.Equals(pattern.Pattern, StringComparison.OrdinalIgnoreCase));

            if (otherPattern is not null && CanBothSupply(pattern.Pattern, publicSource.Name, otherSource.Name, mapping, mappings))
            {
                string mappingFiles = string.Join(", ", new[] { DescribeFile(pattern.ConfigPath), DescribeFile(otherPattern.ConfigPath) }
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                warnings.Add(new NuGetSourceWarning(publicSource.Name, otherSource.Name, publicFile, otherFile,
                    pattern.Pattern, mappingFiles));
            }
        }
    }

    private static bool CanBothSupply(string pattern, string publicName, string otherName, PackageSourceMapping mapping,
        PackageSourceMappingSourceItem[] mappings)
    {
        bool isPrefix = pattern.EndsWith('*');
        string prefix = isPrefix ? pattern[..^1] : pattern;
        if (prefix.Length > MaxPackageIdLength ||
            (prefix.Length < MaxPackageIdLength && !PackageIdValidator.IsValidPackageId(prefix + "a")))
        {
            return false;
        }

        string[] patterns = mappings.SelectMany(item => item.Patterns)
            .Select(item => item.Pattern)
            .ToArray();
        string[] longerPrefixes = patterns
            .Where(item => item.EndsWith('*') && item.Length > pattern.Length)
            .Select(item => item[..^1])
            .ToArray();

        return FindMatchingId(prefix);

        bool FindMatchingId(string candidate)
        {
            if (PackageIdValidator.IsValidPackageId(candidate))
            {
                IReadOnlyList<string> matchingSources = mapping.GetConfiguredPackageSources(candidate);
                if (matchingSources.Contains(publicName, StringComparer.OrdinalIgnoreCase) &&
                    matchingSources.Contains(otherName, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Exact-ID mappings outrank prefixes, so longer prefixes only prune the expansion below, not the check above.
            if (longerPrefixes.Any(item => candidate.StartsWith(item, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (!isPrefix || candidate.Length >= MaxPackageIdLength ||
                !PackageIdValidator.IsValidPackageId(candidate + "a"))
            {
                return false;
            }

            char[] nextCharacters = patterns.Where(item => item.Length > candidate.Length &&
                    item.StartsWith(candidate, StringComparison.OrdinalIgnoreCase) && item[candidate.Length] != '*')
                .Select(item => item[candidate.Length])
                .Distinct()
                .ToArray();
            if (nextCharacters.Any(character => FindMatchingId(candidate + character)))
            {
                return true;
            }

            // A character absent from all remaining patterns represents every branch with no more-specific mapping.
            for (int value = 0; value <= char.MaxValue; value++)
            {
                char character = (char)value;
                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    continue;
                }

                if (nextCharacters.Any(next => char.ToUpperInvariant(next) == char.ToUpperInvariant(character)))
                {
                    continue;
                }

                string nextCandidate = candidate + character;
                if (PackageIdValidator.IsValidPackageId(nextCandidate) &&
                    FindMatchingId(nextCandidate))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool IsNuGetOrg(PackageSource source)
    {
        if (!Uri.TryCreate(source.Source, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        string path = uri.AbsolutePath.TrimEnd('/');
        return (uri.Host.Equals("api.nuget.org", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v3/index.json", StringComparison.OrdinalIgnoreCase)) ||
               ((uri.Host.Equals("www.nuget.org", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("nuget.org", StringComparison.OrdinalIgnoreCase)) &&
                path.Equals("/api/v2", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindSourceFile(ISettings settings, PackageSource source)
    {
        SourceItem? item = settings.GetSection("packageSources")?.Items.OfType<SourceItem>()
            .FirstOrDefault(candidate => candidate.Key.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
        return DescribeFile(item?.ConfigPath);
    }

    private static string DescribeFile(string? path) => string.IsNullOrWhiteSpace(path) ? "unknown configuration file" : path;
}

/// <summary>
/// Describes two sources that may supply the same package and the configuration that made them available.
/// </summary>
internal sealed record NuGetSourceWarning(string PublicSource, string OtherSource, string PublicSourceFile,
    string OtherSourceFile, string Pattern, string MappingFiles);
