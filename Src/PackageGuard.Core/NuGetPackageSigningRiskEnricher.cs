using System.IO.Compression;
using System.Diagnostics;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Versioning;

namespace PackageGuard.Core;

internal sealed class NuGetPackageSigningRiskEnricher(ILogger logger, string? globalPackagesFolder = null) : IEnrichPackageRisk
{
    private static readonly Dictionary<string, ArchiveRiskData?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock CacheLock = new();

    private readonly string globalPackagesFolder = globalPackagesFolder
        ?? Environment.GetEnvironmentVariable("NUGET_PACKAGES")
        ?? SettingsUtility.GetGlobalPackagesFolder(Settings.LoadDefaultSettings(Directory.GetCurrentDirectory()));

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
        package.SupportedTargetFrameworks = archiveRiskData.SupportedTargetFrameworks;
        package.HasModernTargetFrameworkSupport = archiveRiskData.HasModernTargetFrameworkSupport;
        return Task.CompletedTask;
    }

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
                HasModernTargetFrameworkSupport = targetFrameworks.Length == 0 ? null : targetFrameworks.Any(IsModernTargetFramework)
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

    private static bool IsModernTargetFramework(string framework)
    {
        string normalized = framework.ToLowerInvariant();
        return normalized.StartsWith("net8") ||
               normalized.StartsWith("net9") ||
               normalized.StartsWith("net7") ||
               normalized.StartsWith("net6") ||
               normalized.StartsWith("net5") ||
               normalized.StartsWith("netstandard2.0") ||
               normalized.StartsWith("netstandard2.1");
    }

    private sealed class ArchiveRiskData
    {
        public bool? IsSigned { get; init; }

        public bool? HasTrustedSignature { get; init; }

        public string[] SupportedTargetFrameworks { get; init; } = [];

        public bool? HasModernTargetFrameworkSupport { get; init; }
    }
}
