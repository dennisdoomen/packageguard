using System.Text.Json;
using PackageGuard.Core.GitHub;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
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

    /// <summary>
    /// Limits total root and reusable workflows read per repository, including workflows reached through nested calls.
    /// </summary>
    private const int ReusableWorkflowLimit = 20;

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
            ? GitHubRequestTracker.TrackAsync(() => ReadWorkflowFiles(repository, rootFiles))
            : Task.FromResult(new Tracked<GitHubWorkflowFileSignals>(DetectSignals(string.Empty, string.Empty, rootFiles), true));
    }

    /// <summary>Downloads a sample of the workflow files and searches them for the signals.</summary>
    private async Task<GitHubWorkflowFileSignals> ReadWorkflowFiles(GitHubRepositoryContext repository,
        string[] rootFiles)
    {
        try
        {
            using JsonDocument? workflowsDoc = await apiClient.GetJsonAsync(
                $"{repository.ApiRoot}/contents/.github/workflows?ref={repository.EscapedDefaultBranch}",
                fields: GitHubResponseFields.Of("path"));

            if (workflowsDoc is null)
            {
                return DetectSignals(string.Empty, string.Empty, rootFiles);
            }

            if (workflowsDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                GitHubRequestFailureScope.ReportFailure();
                return DetectSignals(string.Empty, string.Empty, rootFiles);
            }

            string[] paths = workflowsDoc.RootElement.EnumerateArray()
                .Select(item => TryReadString(item, "path"))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Where(path => path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (paths.Length > WorkflowFileSampleSize)
            {
                GitHubRequestFailureScope.ReportFailure();
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var contents = new List<string>();
            foreach (string path in paths.Take(WorkflowFileSampleSize))
            {
                await ReadWorkflow(repository, path, visited, contents);
            }

            string combined = string.Join("\n", contents).ToLowerInvariant();
            string dependabotConfig = await dependabot.GetConfigAsync(repository, combined);

            return DetectSignals(combined, dependabotConfig, rootFiles);
        }
        catch
        {
            GitHubRequestFailureScope.ReportFailure();
            return new GitHubWorkflowFileSignals();
        }
    }

    private async Task ReadWorkflow(GitHubRepositoryContext repository, string path,
        HashSet<string> visited, List<string> contents)
    {
        string key = $"{repository.ApiRoot}/{path}@{repository.DefaultBranch}";
        if (!visited.Add(key))
        {
            return;
        }

        if (visited.Count > ReusableWorkflowLimit)
        {
            GitHubRequestFailureScope.ReportFailure();
            return;
        }

        string content = await files.TryGetContentAsync(repository, path);
        if (string.IsNullOrWhiteSpace(content))
        {
            GitHubRequestFailureScope.ReportFailure();
            return;
        }

        contents.Add(content);
        foreach (string reference in ReadReusableWorkflowReferences(content))
        {
            (GitHubRepositoryContext? target, string? targetPath) = ResolveReference(repository, reference);
            if (target is null || targetPath is null)
            {
                GitHubRequestFailureScope.ReportFailure();
            }
            else
            {
                await ReadWorkflow(target, targetPath, visited, contents);
            }
        }
    }

    private static IEnumerable<string> ReadReusableWorkflowReferences(string content)
    {
        var yaml = new YamlStream();
        try
        {
            yaml.Load(new StringReader(content));
        }
        catch (YamlException)
        {
            GitHubRequestFailureScope.ReportFailure();
            return [];
        }

        if (yaml.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root ||
            !root.Children.TryGetValue(new YamlScalarNode("jobs"), out YamlNode? jobsNode) ||
            jobsNode is not YamlMappingNode jobs)
        {
            GitHubRequestFailureScope.ReportFailure();
            return [];
        }

        if (jobs.Children.Values.Any(job => job is not YamlMappingNode) ||
            jobs.Children.Values.OfType<YamlMappingNode>().Any(job =>
                job.Children.TryGetValue(new YamlScalarNode("uses"), out YamlNode? uses) && uses is not YamlScalarNode))
        {
            GitHubRequestFailureScope.ReportFailure();
        }

        return jobs.Children.Values.OfType<YamlMappingNode>()
            .Select(job => job.Children.TryGetValue(new YamlScalarNode("uses"), out YamlNode? uses) ? uses : null)
            .OfType<YamlScalarNode>()
            .Select(uses => uses.Value ?? string.Empty)
            .ToArray();
    }

    private static (GitHubRepositoryContext? Repository, string? Path) ResolveReference(
        GitHubRepositoryContext repository, string reference)
    {
        if (reference.StartsWith("./.github/workflows/", StringComparison.Ordinal) &&
            IsWorkflowFileName(reference["./.github/workflows/".Length..]))
        {
            return (repository, reference[2..]);
        }

        int at = reference.LastIndexOf('@');
        string[] parts = at > 0 ? reference[..at].Split('/') : [];
        if (parts.Length != 5 || parts[2] != ".github" || parts[3] != "workflows" ||
            string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) ||
            !IsWorkflowFileName(parts[4]) || at == reference.Length - 1)
        {
            return (null, null);
        }

        string owner = parts[0];
        string name = parts[1];
        string apiRoot = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";
        return (new GitHubRepositoryContext(apiRoot, reference[(at + 1)..], owner, name, false, string.Empty, null),
            $".github/workflows/{parts[4]}");
    }

    private static bool IsWorkflowFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name is not "." and not ".." && !name.Contains('/') &&
        !name.Contains('\\') && (name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                                name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

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
