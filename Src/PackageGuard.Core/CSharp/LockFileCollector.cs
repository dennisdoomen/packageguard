using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.ProjectModel;

namespace PackageGuard.Core.CSharp;

/// <summary>
/// Finds the C# projects at a path and loads their restore output, and nothing else. Unlike a full policy analysis,
/// it never fetches package metadata or licenses, never touches the package cache and never evaluates policies,
/// so it only does what <c>dotnet restore</c> itself needs.
/// </summary>
public class LockFileCollector
{
    /// <summary>
    /// Gets or sets the logger used to report progress and diagnostics.
    /// </summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Gets or sets whether the .NET restore may prompt for input, such as feed credentials.
    /// </summary>
    public bool InteractiveRestore { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to restore even if the lock file is up-to-date.
    /// </summary>
    public bool ForceRestore { get; set; }

    /// <summary>
    /// Gets or sets whether to never restore, even if the lock file is missing or out-of-date.
    /// </summary>
    public bool SkipRestore { get; set; }

    /// <summary>
    /// Loads the lock file of every project found at <paramref name="projectOrSolutionPath" />. Projects without a
    /// usable lock file are skipped.
    /// </summary>
    public IReadOnlyCollection<LockFile> Collect(string projectOrSolutionPath)
    {
        var loader = new DotNetLockFileLoader
        {
            Logger = Logger,
            InteractiveRestore = InteractiveRestore,
            ForceRestore = ForceRestore,
            SkipRestore = SkipRestore
        };

        return new CSharpProjectScanner(Logger)
            .FindProjects(string.IsNullOrEmpty(projectOrSolutionPath) ? "." : projectOrSolutionPath)
            .Select(loader.GetPackageLockFile)
            .OfType<LockFile>()
            .ToArray();
    }
}
