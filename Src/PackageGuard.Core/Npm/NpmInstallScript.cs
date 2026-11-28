using MemoryPack;

namespace PackageGuard.Core.Npm;

/// <summary>
/// Describes an npm lifecycle script that runs while installing a package.
/// </summary>
[MemoryPackable]
internal sealed partial record NpmInstallScript(string Name, string Command);
