using System.IO.Compression;
using System.Diagnostics;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Versioning;

namespace PackageGuard.Core;

/// <summary>
/// Enriches package risk data by reading signing, target framework, and native binary information from .nupkg archives.
/// </summary>
internal sealed class NuGetPackageSigningRiskEnricher(ILogger logger, string? globalPackagesFolder = null) : IEnrichPackageRisk
{
    /// <summary>
    /// Maps archive path to its extracted risk data, shared across all enricher instances.
    /// </summary>
    private static readonly Dictionary<string, ArchiveRiskData?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Synchronizes read and write access to <see cref="Cache"/>.
    /// </summary>
    private static readonly Lock CacheLock = new();

    /// <summary>
    /// The path to the NuGet global packages folder used to locate .nupkg archives on disk.
    /// </summary>
    private readonly string globalPackagesFolder = globalPackagesFolder
        ?? Environment.GetEnvironmentVariable("NUGET_PACKAGES")
        ?? SettingsUtility.GetGlobalPackagesFolder(Settings.LoadDefaultSettings(Directory.GetCurrentDirectory()));

    /// <summary>
    /// Returns <see langword="true"/> if signing risk data has already been populated for <paramref name="package"/>.
    /// </summary>
    public bool HasCachedData(PackageInfo package) => package.HasSigningRiskData;

    /// <summary>
    /// Reads signing, target framework, and native binary information from the .nupkg archive and populates the corresponding risk fields on <paramref name="package"/>.
    /// </summary>
    public Task EnrichAsync(PackageInfo package)
    {
        if (package.Source.Equals("npm", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        string? packagePath = TryGetPackageArchivePath(package);
        if (packagePath is null)
        {
            return Task.CompletedTask;
        }

        ArchiveRiskData? archiveRiskData;
        lock (CacheLock)
        {
            Cache.TryGetValue(packagePath, out archiveRiskData);
        }

        if (archiveRiskData is null)
        {
            archiveRiskData = ReadArchiveRiskData(packagePath);

            lock (CacheLock)
            {
                Cache[packagePath] = archiveRiskData;
            }
        }

        package.IsPackageSigned = archiveRiskData.IsSigned;
        package.HasTrustedPackageSignature = archiveRiskData.HasTrustedSignature;
        package.HasVerifiedPublisher ??= archiveRiskData.HasTrustedSignature;
        package.SupportedTargetFrameworks = archiveRiskData.SupportedTargetFrameworks;
        package.HasModernTargetFrameworkSupport = archiveRiskData.HasModernTargetFrameworkSupport;
        package.HasNativeBinaryAssets = archiveRiskData.HasNativeBinaryAssets;
        package.HasSigningRiskData = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Finds the .nupkg archive on disk for the given package by trying normalized version strings, or returns <see langword="null"/> if no archive is found.
    /// </summary>
    private string? TryGetPackageArchivePath(PackageInfo package)
    {
        string packageId = package.Name.ToLowerInvariant();
        foreach (string version in EnumerateVersionCandidates(package.Version))
        {
            string path = Path.Combine(globalPackagesFolder, packageId, version, $"{packageId}.{version}.nupkg");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Yields normalized version strings derived from <paramref name="version"/> to use when probing the package cache directory.
    /// </summary>
    private static IEnumerable<string> EnumerateVersionCandidates(string version)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string lowered = version.ToLowerInvariant();
        if (seen.Add(lowered))
        {
            yield return lowered;
        }

        if (NuGetVersion.TryParse(version, out NuGetVersion? parsed))
        {
            string normalized = parsed.ToNormalizedString().ToLowerInvariant();
            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }

    /// <summary>
    /// Opens the .nupkg archive at <paramref name="packagePath"/> and extracts signing status, target framework monikers, and native binary presence.
    /// </summary>
    private ArchiveRiskData ReadArchiveRiskData(string packagePath)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(packagePath);
            bool isSigned = archive.Entries.Any(entry => entry.FullName.Equals(".signature.p7s", StringComparison.OrdinalIgnoreCase));
            string[] targetFrameworks = ReadTargetFrameworks(archive);

            return new ArchiveRiskData
            {
                IsSigned = isSigned,
                HasTrustedSignature = isSigned ? VerifyTrustedSignature(packagePath) : false,
                SupportedTargetFrameworks = targetFrameworks,
                HasModernTargetFrameworkSupport = targetFrameworks.Length == 0 ? null : targetFrameworks.Any(IsModernTargetFramework),
                HasNativeBinaryAssets = archive.Entries.Any(IsNativeBinaryAsset)
            };
        }
        catch (IOException ex)
        {
            logger.LogDebug("Failed to inspect package signature for {PackagePath}: {Error}", packagePath, ex.Message);
            return new ArchiveRiskData();
        }
        catch (InvalidDataException ex)
        {
            logger.LogDebug("Failed to inspect package signature for {PackagePath}: {Error}", packagePath, ex.Message);
            return new ArchiveRiskData();
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug("Failed to inspect package signature for {PackagePath}: {Error}", packagePath, ex.Message);
            return new ArchiveRiskData();
        }
    }

    /// <summary>
    /// Runs <c>dotnet nuget verify</c> against the archive at <paramref name="packagePath"/> and returns <see langword="true"/> on success, <see langword="false"/> on failure, or <see langword="null"/> if verification could not be performed.
    /// </summary>
    private bool? VerifyTrustedSignature(string packagePath)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"nuget verify --all \"{packagePath}\" -v quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            if (!process.WaitForExit(15000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                logger.LogDebug("Timed out while verifying trusted signature for {PackagePath}", packagePath);
                return null;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            logger.LogDebug("Failed to verify trusted signature for {PackagePath}: {Error}", packagePath, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Extracts distinct target framework monikers from <c>lib/</c> and <c>ref/</c> entries inside <paramref name="archive"/>.
    /// </summary>
    private static string[] ReadTargetFrameworks(ZipArchive archive)
    {
        return archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .Where(path => path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) ||
                           path.StartsWith("ref/", StringComparison.OrdinalIgnoreCase))
            .Select(path => path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2)
            .Select(parts => parts[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(framework => framework, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="framework"/> represents <c>netstandard2.x</c> or <c>net5</c> and later.
    /// </summary>
    private static bool IsModernTargetFramework(string framework)
    {
        string normalized = framework.ToLowerInvariant();
        if (normalized.StartsWith("netstandard2.0") || normalized.StartsWith("netstandard2.1"))
        {
            return true;
        }

        Match match = Regex.Match(normalized, @"^net(?<major>\d+)\.(?<minor>\d+)");
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["major"].Value, out int majorVersion))
        {
            return false;
        }

        return majorVersion >= 5;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="entry"/> is a native binary asset such as <c>.so</c>, <c>.dylib</c>, <c>.a</c>, <c>.lib</c>, or <c>.exe</c>.
    /// </summary>
    private static bool IsNativeBinaryAsset(ZipArchiveEntry entry)
    {
        string path = entry.FullName.Replace('\\', '/');
        return path.Contains("/native/", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".a", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".lib", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Holds the signing, target framework, and native binary risk data extracted from a single .nupkg archive.
    /// </summary>
    private sealed class ArchiveRiskData
    {
        /// <summary>
        /// Gets a value indicating whether the archive contains a <c>.signature.p7s</c> entry.
        /// </summary>
        public bool? IsSigned { get; init; }

        /// <summary>
        /// Gets a value indicating whether <c>dotnet nuget verify</c> reported a trusted signature.
        /// </summary>
        public bool? HasTrustedSignature { get; init; }

        /// <summary>
        /// Gets the distinct target framework monikers found under <c>lib/</c> and <c>ref/</c> in the archive.
        /// </summary>
        public string[] SupportedTargetFrameworks { get; init; } = [];

        /// <summary>
        /// Gets a value indicating whether at least one supported target framework is <c>netstandard2.x</c> or <c>net5</c> and later, or <see langword="null"/> when no framework entries are present.
        /// </summary>
        public bool? HasModernTargetFrameworkSupport { get; init; }

        /// <summary>
        /// Gets a value indicating whether the archive contains native binary assets such as <c>.so</c>, <c>.dylib</c>, <c>.a</c>, <c>.lib</c>, or <c>.exe</c> files.
        /// </summary>
        public bool HasNativeBinaryAssets { get; init; }
    }
}
