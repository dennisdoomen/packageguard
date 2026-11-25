using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.GitHub;
using PackageGuard.Core.Risk.Enrichment.GitHub;
using PackageGuard.Specs.Common;

namespace PackageGuard.Specs.GitHub;

[TestClass]
public class GitHubWorkflowFileCollectorSpecs
{
    private static readonly GitHubRepositoryContext Repository =
        new("https://api.github.com/repos/acme/widget", "main", "acme", "widget", true, "", null);

    [TestMethod]
    public async Task Follows_cross_repository_and_local_workflows_without_following_cycles()
    {
        var handler = new ScriptedHttpMessageHandler((request, _) => Respond(request));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        var collector = new GitHubWorkflowFileCollector(client);

        Tracked<GitHubWorkflowFileSignals> result = await collector.CollectAsync(Repository, [".github"]);

        result.IsComplete.Should().BeTrue();
        result.Value.HasTestSignal.Should().BeTrue();
        result.Value.PlatformCount.Should().Be(3);
        handler.RequestedUrls.Should().ContainSingle(url => url.Contains("other/shared/contents"));
    }

    [TestMethod]
    public async Task Leaves_workflow_signals_unknown_when_a_reference_is_inaccessible()
    {
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.ToString().Contains("other/shared") ? ScriptedResponse.NotFound() : Respond(request));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);
        var collector = new GitHubWorkflowFileCollector(client);

        Tracked<GitHubWorkflowFileSignals> result = await collector.CollectAsync(Repository, [".github"]);

        result.IsComplete.Should().BeFalse();
    }

    [TestMethod]
    public async Task Leaves_workflow_signals_unknown_when_the_root_sample_is_truncated()
    {
        var handler = new ScriptedHttpMessageHandler((request, _) =>
            request.RequestUri!.ToString().Contains("/contents/.github/workflows?")
                ? ScriptedResponse.Json("""
                    [{"path":".github/workflows/1.yml"},{"path":".github/workflows/2.yml"},
                     {"path":".github/workflows/3.yml"},{"path":".github/workflows/4.yml"},
                     {"path":".github/workflows/5.yml"},{"path":".github/workflows/6.yml"}]
                    """)
                : Workflow("jobs: {}\n"));
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);

        Tracked<GitHubWorkflowFileSignals> result =
            await new GitHubWorkflowFileCollector(client).CollectAsync(Repository, [".github"]);

        result.IsComplete.Should().BeFalse();
    }

    [TestMethod]
    public async Task Limits_the_number_of_reusable_workflows_fetched()
    {
        var handler = new ScriptedHttpMessageHandler((request, _) =>
        {
            string url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            if (url.Contains("/contents/.github/workflows?"))
            {
                return ScriptedResponse.Json("""[{"path":".github/workflows/0.yml"}]""");
            }

            if (url.Contains("/contents/.github/workflows/"))
            {
                string fileName = url.Split('?')[0].Split('/')[^1];
                int index = int.Parse(fileName[..^4]);
                return Workflow($"jobs:\n  next:\n    uses: ./.github/workflows/{index + 1}.yml\n");
            }

            return ScriptedResponse.NotFound();
        });
        using var client = new GitHubApiClient(NullLogger.Instance, null, null, handler);

        Tracked<GitHubWorkflowFileSignals> result =
            await new GitHubWorkflowFileCollector(client).CollectAsync(Repository, [".github"]);

        result.IsComplete.Should().BeFalse();
        handler.RequestedUrls.Should().HaveCount(22);
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request)
    {
        string url = Uri.UnescapeDataString(request.RequestUri!.ToString());
        if (url.Contains("/contents/.github/workflows?"))
        {
            return ScriptedResponse.Json("""[{"path":".github/workflows/main.yml"}]""");
        }

        if (url.Contains("/contents/.github/workflows/main.yml"))
        {
            return Workflow("jobs:\n  build:\n    uses: other/shared/.github/workflows/build.yml@v1\n");
        }

        if (url.Contains("/contents/.github/workflows/build.yml"))
        {
            return Workflow("jobs:\n  build:\n    runs-on: [ubuntu-latest, windows-latest, macos-latest]\n" +
                            "    steps:\n      - run: dotnet test\n  next:\n    uses: ./.github/workflows/build.yml\n");
        }

        return ScriptedResponse.NotFound();
    }

    private static HttpResponseMessage Workflow(string content) =>
        ScriptedResponse.Json($$"""{"content":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes(content))}}"}""");
}
