using System.Text.Json;
using PackageGuard.Core.GitHub;
using static PackageGuard.Core.Common.JsonElementReader;

namespace PackageGuard.Core.Risk.Enrichment.GitHub;

/// <summary>
/// Collects what a repository's workflow files reveal about testing, coverage, reproducible builds, provenance and
/// dependency update automation.
/// </summary>
internal sealed class GitHubWorkflowFileCollector
{
    /// <summary>
    /// The number of workflow files that are downloaded. The signals read from them are keyword matches that
    /// saturate after a handful of files.
    /// </summary>
    private const int WorkflowFileSampleSize = 5;

    private readonly GitHubApiClient apiClient;
    private readonly GitHubRepositoryFiles files;
    private readonly GitHubDependabotCollector dependabot;

    /// <summary>Creates a collector that sends its requests through the given client.</summary>
    /// <param name="apiClient">The client to send GitHub API requests through.</param>
    public GitHubWorkflowFileCollector(GitHubApiClient apiClient)
    {
        this.apiClient = apiClient;
        files = new GitHubRepositoryFiles(apiClient);
        dependabot = new GitHubDependabotCollector(apiClient);
    }

    /// <summary>
    /// Reads the workflow YAML files and detects signals for testing, coverage, reproducibility, and dependency
    /// automation. A repository without a <c>.github</c> directory has no workflows, so nothing is requested.
    /// </summary>
    public Task<Tracked<GitHubWorkflowFileSignals>> CollectAsync(GitHubRepositoryContext repository, string[] rootFiles)
    {
        return rootFiles.Contains(".github", StringComparer.OrdinalIgnoreCase)
            ? GitHubRequestTracker.TrackAsync(() => ReadWorkflowFilesAsync(repository, rootFiles))
            : Task.FromResult(new Tracked<GitHubWorkflowFileSignals>(new GitHubWorkflowFileSignals(), true));
    }

    /// <summary>Downloads a sample of the workflow files and searches them for the signals.</summary>
    private async Task<GitHubWorkflowFileSignals> ReadWorkflowFilesAsync(GitHubRepositoryContext repository,
        string[] rootFiles)
    {
        try
        {
            using JsonDocument? workflowsDoc = await apiClient.GetJsonAsync(
                $"{repository.ApiRoot}/contents/.github/workflows?ref={repository.EscapedDefaultBranch}",
                fields: GitHubResponseFields.Of("path"));

            if (workflowsDoc?.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new GitHubWorkflowFileSignals();
            }

            string[] paths = workflowsDoc.RootElement.EnumerateArray()
                .Select(item => TryReadString(item, "path"))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Take(WorkflowFileSampleSize)
                .ToArray();

            string[] contents =
                await Task.WhenAll(paths.Select(path => files.TryGetContentAsync(repository, path)));

            string combined = string.Join("\n", contents).ToLowerInvariant();
            string dependabotConfig = await dependabot.GetConfigAsync(repository, combined);

            return DetectSignals(combined, dependabotConfig, rootFiles);
        }
        catch
        {
            return new GitHubWorkflowFileSignals();
        }
    }

    /// <summary>Searches the combined workflow content and the root listing for the workflow signals.</summary>
    private static GitHubWorkflowFileSignals DetectSignals(string combined, string dependabotConfig, string[] rootFiles)
    {
        return new GitHubWorkflowFileSignals
        {
            HasProvenanceAttestation = ContainsAny(combined, "slsa", "provenance", "attest"),
            HasCoverageSignal = ContainsAny(combined, "coverage", "codecov", "coveralls", "coverlet"),
            HasReproducibleBuildSignal = ContainsAny(combined, "deterministic", "reproducible", "source-build"),
            HasDependencyUpdateAutomation = !string.IsNullOrWhiteSpace(dependabotConfig) ||
                                            ContainsAny(combined, "dependabot", "renovate") ||
                                            HasAnyRootFile(rootFiles, "renovate.json", "renovate.json5"),
            HasTestSignal = ContainsAny(combined, "dotnet test", "npm test", "pnpm test", "yarn test", "pytest", "junit") ||
                            HasAnyRootFile(rootFiles, "test", "tests", "spec", "specs"),
            PlatformCount = CountPlatforms(combined)
        };
    }

    /// <summary>Counts the distinct operating system platforms the workflows run on.</summary>
    private static int CountPlatforms(string combined) =>
        new[] { "ubuntu", "windows", "macos", "self-hosted" }
            .Count(platform => combined.Contains(platform, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true if the content contains any of the keywords, ignoring case.</summary>
    private static bool ContainsAny(string content, params string[] keywords) =>
        keywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true if the repository root contains any of the given files or directories, ignoring case.</summary>
    private static bool HasAnyRootFile(string[] rootFiles, params string[] names) =>
        names.Any(name => rootFiles.Contains(name, StringComparer.OrdinalIgnoreCase));
}
