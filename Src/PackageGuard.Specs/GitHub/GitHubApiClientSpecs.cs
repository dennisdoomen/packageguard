using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.GitHub;
using PackageGuard.Specs.Common;

namespace PackageGuard.Specs.GitHub;

[TestClass]
public class GitHubApiClientSpecs
{
    private const string Url = "https://api.github.com/repos/acme/widget";

    [TestMethod]
    public async Task Returns_the_parsed_response_of_a_successful_request()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((_, _) => ScriptedResponse.Json("""{"name":"widget"}"""));
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);

        // Act
        using JsonDocument document = await client.GetJsonAsync(Url);

        // Assert
        document!.RootElement.GetProperty("name").GetString().Should().Be("widget");
    }

    [TestMethod]
    public async Task Retries_a_request_that_hits_the_secondary_rate_limit()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((_, attempt) => attempt == 1
            ? ScriptedResponse.SecondaryRateLimited()
            : ScriptedResponse.Json("""{"name":"widget"}"""));

        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);

        // Act
        using JsonDocument document = await client.GetJsonAsync(Url);

        // Assert
        document.Should().NotBeNull();
        handler.RequestCount.Should().Be(2);
    }

    [TestMethod]
    public async Task Stops_requesting_once_the_budget_is_spent_and_the_reset_is_far_away()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() =>
            ScriptedResponse.PrimaryRateLimited(TimeSpan.FromMinutes(45)));

        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);

        // Act
        using JsonDocument first = await client.GetJsonAsync(Url);
        using JsonDocument second = await client.GetJsonAsync("https://api.github.com/repos/acme/gadget");

        // Assert
        first.Should().BeNull("the budget was spent");
        second.Should().BeNull("no further requests should be attempted");
        client.IsExhausted.Should().BeTrue();
        handler.RequestCount.Should().Be(1, "the second URL should never reach the network");
    }

    [TestMethod]
    public async Task Skips_optional_requests_once_the_budget_drops_into_the_reserve()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() =>
            ScriptedResponse.WithRateLimit(ScriptedResponse.Json("{}"), remaining: 12));

        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);
        await client.GetJsonAsync(Url);

        // Act
        using JsonDocument optional = await client.GetJsonAsync("https://api.github.com/repos/acme/widget/issues/1/timeline",
            GitHubRequestImportance.Optional);

        using JsonDocument essential = await client.GetJsonAsync("https://api.github.com/repos/acme/gadget");

        // Assert
        optional.Should().BeNull("optional signals make way for the packages still to be scanned");
        essential.Should().NotBeNull("essential requests keep going while the budget allows any request");
        handler.RequestCount.Should().Be(2);
    }

    [TestMethod]
    public async Task Replays_the_cached_body_when_the_resource_did_not_change()
    {
        // Arrange
        var handler = new ScriptedHttpMessageHandler((_, attempt) => attempt == 1
            ? ScriptedResponse.Json("""{"name":"widget"}""", eTag: "\"abc123\"")
            : ScriptedResponse.NotModified());

        var cache = new GitHubResponseCache(NullLogger.Instance);
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, cache, handler);
        await client.GetJsonAsync(Url);

        // Act
        using JsonDocument revalidated = await client.GetJsonAsync(Url);

        // Assert
        revalidated!.RootElement.GetProperty("name").GetString().Should().Be("widget");
        handler.Requests.Last().Headers.IfNoneMatch.ToString().Should().Be("\"abc123\"");
    }

    [TestMethod]
    public async Task Remembers_that_a_resource_does_not_exist()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(ScriptedResponse.NotFound);
        var cache = new GitHubResponseCache(NullLogger.Instance);
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, cache, handler);

        // Act
        await client.GetJsonAsync(Url);
        await client.GetJsonAsync(Url);

        // Assert
        handler.RequestCount.Should().Be(1, "a known-missing resource should not be probed again");
    }

    [TestMethod]
    public async Task Keeps_the_number_of_concurrent_requests_below_the_secondary_rate_limit()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json("{}"));
        using var client = new GitHubApiClient(NullLogger.Instance, apiKey: null, responseCache: null, handler);

        Task<JsonDocument>[] requests = Enumerable.Range(0, 60)
            .Select(index => client.GetJsonAsync($"https://api.github.com/repos/acme/widget/pulls/{index}"))
            .ToArray();

        // Act
        JsonDocument[] responses = await Task.WhenAll(requests);

        // Assert
        handler.RequestCount.Should().Be(60);
        handler.PeakConcurrentRequestCount.Should().BeLessThanOrEqualTo(8);

        foreach (JsonDocument response in responses)
        {
            response.Dispose();
        }
    }

    [TestMethod]
    public async Task Sends_the_configured_token_as_a_bearer_credential()
    {
        // Arrange
        var handler = ScriptedHttpMessageHandler.AlwaysReturns(() => ScriptedResponse.Json("{}"));
        using var client = new GitHubApiClient(NullLogger.Instance, "secret-token", responseCache: null, handler);

        // Act
        await client.GetJsonAsync(Url);

        // Assert
        handler.Requests.Single().Headers.Authorization!.ToString().Should().Be("Bearer secret-token");
    }
}
