using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PackageGuard.Core.GitHub;

/// <summary>
/// Sends requests to the GitHub API on behalf of every component that needs GitHub data, so that the rate limit
/// budget, the request concurrency, and the conditional-request cache are shared across the whole run.
/// </summary>
/// <remarks>
/// GitHub enforces a secondary rate limit on concurrent requests on top of the hourly primary limit, and answers
/// both with <c>403</c> or <c>429</c>. This client keeps concurrency well below that ceiling, honours
/// <c>retry-after</c>, and stops making requests altogether once the budget is spent, rather than spending the rest
/// of the run collecting rejections.
/// </remarks>
internal sealed class GitHubApiClient : IDisposable
{
    private readonly ILogger logger;
    private readonly string? apiKey;
    private readonly GitHubResponseCache responseCache;
    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim concurrencyGate = new(MaxConcurrentRequests);
    private readonly Lock exhaustionLock = new();

    /// <summary>
    /// The rate limit budget as last reported by the API.
    /// </summary>
    private readonly GitHubRateLimit rateLimit = new();

    private bool isExhausted;
    private bool hasLoggedExhaustion;

    /// <summary>
    /// The number of requests allowed to be in flight at the same time. GitHub asks callers to stay below 100
    /// concurrent requests; staying an order of magnitude below that avoids the secondary rate limit entirely.
    /// </summary>
    private const int MaxConcurrentRequests = 8;

    /// <summary>
    /// The number of times a single request is retried after a throttling or transient server response.
    /// </summary>
    private const int MaxAttempts = 3;

    /// <summary>
    /// The longest a single request waits for a rate limit to clear. A wait beyond this points at the hourly primary
    /// limit rather than a short burst limit, and the run degrades instead of stalling.
    /// </summary>
    private static readonly TimeSpan MaxThrottleWait = TimeSpan.FromSeconds(90);

    /// <summary>
    /// The endpoint of GitHub's GraphQL API.
    /// </summary>
    private const string GraphQlUrl = "https://api.github.com/graphql";

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubApiClient"/> class.
    /// </summary>
    /// <param name="logger">The logger to report throttling and request failures to.</param>
    /// <param name="apiKey">The GitHub personal access token to authenticate with, if any.</param>
    /// <param name="responseCache">The conditional-request cache to revalidate responses against.</param>
    /// <param name="handler">An optional message handler, used by the tests to avoid real network traffic.</param>
    public GitHubApiClient(ILogger logger, string? apiKey, GitHubResponseCache? responseCache = null,
        HttpMessageHandler? handler = null)
    {
        this.logger = logger;
        this.apiKey = apiKey;
        this.responseCache = responseCache ?? new GitHubResponseCache(logger);

        httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PackageGuard", "v1"));
    }

    /// <summary>
    /// Indicates whether the rate limit budget is spent and no further requests will be attempted this run.
    /// </summary>
    public bool IsExhausted
    {
        get
        {
            lock (exhaustionLock)
            {
                return isExhausted;
            }
        }
    }

    /// <summary>
    /// Indicates whether the GraphQL API is available. GitHub rejects unauthenticated GraphQL requests outright, so
    /// callers fall back to the REST API when no token is configured.
    /// </summary>
    public bool SupportsGraphQl => !string.IsNullOrWhiteSpace(apiKey);

    /// <summary>
    /// Runs a GraphQL query and returns its <c>data</c> element, or <see langword="null"/> when the query could not
    /// be run or came back with errors.
    /// </summary>
    /// <param name="query">The GraphQL query document.</param>
    /// <param name="variables">The query variables, serialised as the GraphQL <c>variables</c> object.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<JsonDocument?> PostGraphQlAsync(string query, IReadOnlyDictionary<string, object> variables,
        CancellationToken cancellationToken = default)
    {
        if (!SupportsGraphQl || IsExhausted)
        {
            return null;
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            GitHubAttemptResult result = await SendGraphQlOnceAsync(query, variables, cancellationToken);
            if (result.IsFinal)
            {
                return ParseGraphQlPayload(result.Body);
            }

            if (!await WaitBeforeRetryAsync(GraphQlUrl, result, attempt, cancellationToken))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Sends a single GraphQL request, mapping the response onto an attempt result.
    /// </summary>
    private async Task<GitHubAttemptResult> SendGraphQlOnceAsync(string query,
        IReadOnlyDictionary<string, object> variables, CancellationToken cancellationToken)
    {
        await concurrencyGate.WaitAsync(cancellationToken);
        try
        {
            using HttpRequestMessage request = CreateGraphQlRequest(query, variables);
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            rateLimit.Update(response.Headers);

            return response.IsSuccessStatusCode
                ? GitHubAttemptResult.Final(await response.Content.ReadAsStringAsync(cancellationToken))
                : InterpretFailure(GraphQlUrl, response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                  !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "GraphQL request failed");
            return GitHubAttemptResult.Retryable();
        }
        finally
        {
            concurrencyGate.Release();
        }
    }

    /// <summary>
    /// Builds the POST request carrying the GraphQL query and its variables.
    /// </summary>
    private HttpRequestMessage CreateGraphQlRequest(string query, IReadOnlyDictionary<string, object> variables)
    {
        string payload = JsonSerializer.Serialize(new { query, variables });

        HttpRequestMessage request = new(HttpMethod.Post, GraphQlUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    /// <summary>
    /// Parses a GraphQL response, returning its <c>data</c> element and discarding responses that report errors.
    /// </summary>
    private JsonDocument? ParseGraphQlPayload(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using JsonDocument payload = JsonDocument.Parse(body);
            LogGraphQlErrors(payload);

            return payload.RootElement.TryGetProperty("data", out JsonElement data) &&
                   data.ValueKind == JsonValueKind.Object
                ? JsonDocument.Parse(data.GetRawText())
                : null;
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "GraphQL returned a malformed response");
            return null;
        }
    }

    /// <summary>
    /// Reports the errors a GraphQL response carried, which can accompany partial data.
    /// </summary>
    private void LogGraphQlErrors(JsonDocument payload)
    {
        if (payload.RootElement.TryGetProperty("errors", out JsonElement errors) &&
            errors.ValueKind == JsonValueKind.Array)
        {
            logger.LogDebug("GraphQL reported errors: {Errors}", errors.GetRawText());
        }
    }

    /// <summary>
    /// Requests <paramref name="url"/> and returns the parsed response, or <see langword="null"/> when the resource
    /// does not exist, the request failed, or the rate limit budget no longer allows it.
    /// </summary>
    /// <param name="url">The absolute GitHub API URL to request.</param>
    /// <param name="importance">How valuable the request is when the budget runs low.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<JsonDocument?> GetJsonAsync(string url,
        GitHubRequestImportance importance = GitHubRequestImportance.Essential,
        CancellationToken cancellationToken = default)
    {
        string? body = await GetStringAsync(url, importance, cancellationToken);
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "GitHub returned a malformed response for {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Requests <paramref name="url"/> and returns the raw response body, or <see langword="null"/> when the resource
    /// does not exist, the request failed, or the rate limit budget no longer allows it.
    /// </summary>
    /// <param name="url">The absolute GitHub API URL to request.</param>
    /// <param name="importance">How valuable the request is when the budget runs low.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    private async Task<string?> GetStringAsync(string url, GitHubRequestImportance importance,
        CancellationToken cancellationToken = default)
    {
        string? alreadyFetched = responseCache.FindFreshBody(url);
        if (alreadyFetched is not null)
        {
            return alreadyFetched;
        }

        if (!ShouldAttempt(url, importance))
        {
            return null;
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            GitHubAttemptResult result = await SendOnceAsync(url, cancellationToken);
            if (result.IsFinal)
            {
                return result.Body;
            }

            if (!await WaitBeforeRetryAsync(url, result, attempt, cancellationToken))
            {
                return null;
            }
        }

        logger.LogDebug("Giving up on {Url} after {Attempts} attempts", url, MaxAttempts);
        return null;
    }

    /// <summary>
    /// Returns <see langword="false"/> when the request should not even be attempted, because the budget is spent,
    /// the budget is down to its reserve and the request is optional, or the resource is known to be missing.
    /// </summary>
    private bool ShouldAttempt(string url, GitHubRequestImportance importance)
    {
        if (IsExhausted)
        {
            return false;
        }

        if (importance == GitHubRequestImportance.Optional && rateLimit.IsInReserve())
        {
            logger.LogDebug("Skipping optional GitHub request {Url}; only {Remaining} requests left before the reset",
                url, rateLimit.Remaining);

            return false;
        }

        return !responseCache.IsKnownToBeMissing(url);
    }

    /// <summary>
    /// Performs a single attempt, translating the response into either a final answer or a retry instruction.
    /// </summary>
    private async Task<GitHubAttemptResult> SendOnceAsync(string url, CancellationToken cancellationToken)
    {
        await concurrencyGate.WaitAsync(cancellationToken);
        try
        {
            return await SendAndInterpretAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                  !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "GitHub request to {Url} failed", url);
            return GitHubAttemptResult.Retryable();
        }
        finally
        {
            concurrencyGate.Release();
        }
    }

    /// <summary>
    /// Sends the conditional request and maps the status code onto an attempt result.
    /// </summary>
    private async Task<GitHubAttemptResult> SendAndInterpretAsync(string url, CancellationToken cancellationToken)
    {
        GitHubResponseCacheEntry? cached = responseCache.Find(url);

        logger.LogDebug("GET {Url}", url);
        using HttpRequestMessage request = CreateRequest(url, cached?.ETag);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        rateLimit.Update(response.Headers);

        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            responseCache.MarkAsRevalidated(url);
            return GitHubAttemptResult.Final(cached.Body);
        }

        return response.IsSuccessStatusCode
            ? GitHubAttemptResult.Final(await StoreSuccessAsync(url, response, cancellationToken))
            : InterpretFailure(url, response);
    }

    /// <summary>
    /// Reads and caches the body of a successful response.
    /// </summary>
    private async Task<string> StoreSuccessAsync(string url, HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        responseCache.StoreResponse(url, response.Headers.ETag?.ToString(), body);
        return body;
    }

    /// <summary>
    /// Maps an unsuccessful response onto an attempt result, remembering missing resources and recognising throttling.
    /// </summary>
    private GitHubAttemptResult InterpretFailure(string url, HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            responseCache.StoreNotFound(url);
            return GitHubAttemptResult.Final(null);
        }

        if (IsThrottled(response))
        {
            return GitHubAttemptResult.Throttled(ReadRetryAfter(response));
        }

        if ((int)response.StatusCode >= 500)
        {
            logger.LogDebug("GitHub returned {StatusCode} for {Url}", (int)response.StatusCode, url);
            return GitHubAttemptResult.Retryable();
        }

        logger.LogDebug("GitHub returned {StatusCode} for {Url}", (int)response.StatusCode, url);
        return GitHubAttemptResult.Final(null);
    }

    /// <summary>
    /// Waits for the delay a throttled or transient response asks for, and reports whether retrying is still viable.
    /// </summary>
    private async Task<bool> WaitBeforeRetryAsync(string url, GitHubAttemptResult result, int attempt,
        CancellationToken cancellationToken)
    {
        if (!result.WasThrottled)
        {
            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            return true;
        }

        TimeSpan delay = result.RetryAfter ?? rateLimit.TimeUntilReset(DateTimeOffset.UtcNow) ??
            TimeSpan.FromSeconds(attempt * 2);

        if (delay > MaxThrottleWait)
        {
            ReportExhaustion(delay);
            return false;
        }

        logger.LogDebug("GitHub throttled {Url}; waiting {Delay} before retrying", url, delay);
        await Task.Delay(delay, cancellationToken);
        return true;
    }

    /// <summary>
    /// Stops all further requests for this run and explains to the user how to lift the limit.
    /// </summary>
    private void ReportExhaustion(TimeSpan delay)
    {
        lock (exhaustionLock)
        {
            isExhausted = true;
            if (hasLoggedExhaustion)
            {
                return;
            }

            hasLoggedExhaustion = true;
        }

        LogExhaustion(delay);
    }

    /// <summary>
    /// Writes the one-off warning that explains why GitHub data is missing from the results.
    /// </summary>
    private void LogExhaustion(TimeSpan delay)
    {
        if (apiKey is null)
        {
            logger.LogWarning(
                "The GitHub API rate limit is exhausted and resets in {Minutes} minutes. Unauthenticated callers get " +
                "only 60 requests per hour. Pass a personal access token through --github-api-key or the " +
                "GITHUB_API_KEY environment variable to raise the limit to 5000 requests per hour. GitHub-based " +
                "signals are left empty for the remaining packages.",
                Math.Ceiling(delay.TotalMinutes));
        }
        else
        {
            logger.LogWarning(
                "The GitHub API rate limit is exhausted and resets in {Minutes} minutes. GitHub-based signals are " +
                "left empty for the remaining packages. Re-run after the reset to complete the report.",
                Math.Ceiling(delay.TotalMinutes));
        }
    }

    /// <summary>
    /// Builds a request carrying the GitHub media type, the token when configured, and the cached entity tag.
    /// </summary>
    private HttpRequestMessage CreateRequest(string url, string? eTag)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        if (!string.IsNullOrWhiteSpace(eTag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", eTag);
        }

        return request;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the response signals a primary or secondary rate limit rather than a
    /// permission problem. Both arrive as <c>403</c> or <c>429</c>, distinguished by the rate limit headers.
    /// </summary>
    private static bool IsThrottled(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (response.StatusCode != HttpStatusCode.Forbidden)
        {
            return false;
        }

        return response.Headers.Contains("retry-after") || ReadRemaining(response) == 0;
    }

    /// <summary>
    /// Reads the <c>retry-after</c> header as a delay, when present.
    /// </summary>
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is not null)
        {
            return retryAfter.Delta;
        }

        return retryAfter?.Date is null ? null : retryAfter.Date.Value - DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Reads the remaining request budget straight off a response, independent of the tracked state.
    /// </summary>
    private static int? ReadRemaining(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? values))
        {
            return null;
        }

        return int.TryParse(values.FirstOrDefault(), out int remaining) ? remaining : null;
    }

    /// <summary>
    /// Releases the underlying HTTP client and concurrency gate.
    /// </summary>
    public void Dispose()
    {
        httpClient.Dispose();
        concurrencyGate.Dispose();
    }

    /// <summary>
    /// The outcome of a single request attempt.
    /// </summary>
    /// <param name="IsFinal">Indicates that no further attempt should be made.</param>
    /// <param name="Body">The response body when the attempt succeeded.</param>
    /// <param name="WasThrottled">Indicates that GitHub rejected the attempt because of a rate limit.</param>
    /// <param name="RetryAfter">The delay GitHub asked the caller to wait, when it supplied one.</param>
    private sealed record GitHubAttemptResult(bool IsFinal, string? Body, bool WasThrottled, TimeSpan? RetryAfter)
    {
        /// <summary>
        /// Creates a result that ends the retry loop.
        /// </summary>
        public static GitHubAttemptResult Final(string? body) => new(true, body, false, null);

        /// <summary>
        /// Creates a result asking for another attempt after a short backoff.
        /// </summary>
        public static GitHubAttemptResult Retryable() => new(false, null, false, null);

        /// <summary>
        /// Creates a result asking for another attempt once the rate limit clears.
        /// </summary>
        public static GitHubAttemptResult Throttled(TimeSpan? retryAfter) => new(false, null, true, retryAfter);
    }
}
