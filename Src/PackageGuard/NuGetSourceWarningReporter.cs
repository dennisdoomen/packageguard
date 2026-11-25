using Microsoft.Extensions.Logging;
using PackageGuard.Core.CSharp;
using PackageGuard.Core.Policy;

namespace PackageGuard;

/// <summary>
/// Reports NuGet source warnings and records whether an affected project treats them as errors.
/// </summary>
internal sealed class NuGetSourceWarningReporter(ILogger logger)
{
    private const string DocumentationUrl = "https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping";
    private readonly NuGetSourceMappingChecker checker = new();
    private readonly Dictionary<NuGetSourceWarning, List<string>> projectsByWarning = new();
    private readonly HashSet<string> failingProjects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the projects configured to fail when their source configuration has a warning.
    /// </summary>
    public IReadOnlyCollection<string> FailingProjects => failingProjects;

    /// <summary>
    /// Checks one project before restore and logs each distinct finding once.
    /// </summary>
    public void CheckProject(string projectPath, ProjectPolicy policy)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        foreach (NuGetSourceWarning warning in checker.Check(directory))
        {
            if (!projectsByWarning.TryGetValue(warning, out List<string>? projects))
            {
                projects = [];
                projectsByWarning.Add(warning, projects);
                LogWarning(warning, projectPath);
            }

            projects.Add(projectPath);
            if (policy.FailOnNugetSourceWarnings == true)
            {
                failingProjects.Add(projectPath);
            }
        }
    }

    /// <summary>
    /// Logs projects sharing a finding and returns whether a project configured it to fail the run.
    /// </summary>
    public bool Complete()
    {
        foreach ((NuGetSourceWarning warning, List<string> projects) in projectsByWarning)
        {
            if (projects.Count > 1)
            {
                logger.LogWarning("NuGet source warning for {Feed} also affects projects: {Projects}",
                    warning.OtherSource, string.Join(", ", projects));
            }
        }

        return failingProjects.Count > 0;
    }

    private void LogWarning(NuGetSourceWarning warning, string projectPath)
    {
        string overlap = string.IsNullOrEmpty(warning.Pattern)
            ? string.IsNullOrEmpty(warning.MappingFiles)
                ? "no package source mapping is enabled"
                : $"no package source mapping is enabled (mapping configuration: {warning.MappingFiles})"
            : $"both sources map the pattern '{warning.Pattern}' (mapping configuration: {warning.MappingFiles})";

        logger.LogWarning(
            "NuGet source '{PublicSource}' ({PublicFile}) and source '{OtherSource}' ({OtherFile}) may supply the same " +
            "packages for {Project}: {Overlap}. Configure package source mapping: {DocumentationUrl}",
            warning.PublicSource, warning.PublicSourceFile, warning.OtherSource, warning.OtherSourceFile, projectPath, overlap,
            DocumentationUrl);
    }
}
