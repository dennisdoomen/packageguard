using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PackageGuard.Specs.Common;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a script instead of the network, and records what it was
/// asked for.
/// </summary>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> respond;
    private readonly ConcurrentQueue<HttpRequestMessage> requests = new();
    private int requestCount;
    private int concurrentRequestCount;
    private int peakConcurrentRequestCount;

    /// <summary>
    /// Creates a handler that calls <paramref name="respond"/> for every request, passing the one-based index of the
    /// request so that a script can answer differently on a retry.
    /// </summary>
    public ScriptedHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        this.respond = respond;
    }

    /// <summary>
    /// Creates a handler that always answers with <paramref name="response"/>.
    /// </summary>
    public static ScriptedHttpMessageHandler AlwaysReturns(Func<HttpResponseMessage> response) =>
        new((_, _) => response());

    /// <summary>
    /// The requests received so far, in the order they arrived.
    /// </summary>
    public IReadOnlyList<HttpRequestMessage> Requests => requests.ToArray();

    /// <summary>
    /// The number of requests received so far.
    /// </summary>
    public int RequestCount => Volatile.Read(ref requestCount);

    /// <summary>
    /// The highest number of requests that were in flight at the same time.
    /// </summary>
    public int PeakConcurrentRequestCount => Volatile.Read(ref peakConcurrentRequestCount);

    /// <summary>
    /// The URLs of the requests received so far.
    /// </summary>
    public IReadOnlyList<string> RequestedUrls => requests.Select(request => request.RequestUri!.ToString()).ToArray();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        requests.Enqueue(request);
        int index = Interlocked.Increment(ref requestCount);
        TrackPeakConcurrency(Interlocked.Increment(ref concurrentRequestCount));

        try
        {
            await Task.Yield();
            return respond(request, index);
        }
        finally
        {
            Interlocked.Decrement(ref concurrentRequestCount);
        }
    }

    /// <summary>
    /// Raises the recorded peak whenever the current number of in-flight requests exceeds it.
    /// </summary>
    private void TrackPeakConcurrency(int current)
    {
        int peak = Volatile.Read(ref peakConcurrentRequestCount);
        while (current > peak)
        {
            int previous = Interlocked.CompareExchange(ref peakConcurrentRequestCount, current, peak);
            if (previous == peak)
            {
                return;
            }

            peak = previous;
        }
    }
}

/// <summary>
/// Builds the HTTP responses that <see cref="ScriptedHttpMessageHandler"/> hands out.
/// </summary>
internal static class ScriptedResponse
{
    /// <summary>
    /// Builds a successful JSON response, optionally carrying an entity tag.
    /// </summary>
    public static HttpResponseMessage Json(string body, string eTag = null)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StringContent(body) };
        if (eTag is not null)
        {
            response.Headers.TryAddWithoutValidation("ETag", eTag);
        }

        return WithRateLimit(response, remaining: 4000);
    }

    /// <summary>
    /// Builds a <c>304 Not Modified</c> response.
    /// </summary>
    public static HttpResponseMessage NotModified() =>
        WithRateLimit(new HttpResponseMessage(HttpStatusCode.NotModified), remaining: 4000);

    /// <summary>
    /// Builds a <c>404 Not Found</c> response.
    /// </summary>
    public static HttpResponseMessage NotFound() =>
        WithRateLimit(new HttpResponseMessage(HttpStatusCode.NotFound), remaining: 4000);

    /// <summary>
    /// Builds the <c>403</c> that GitHub sends for a secondary rate limit, asking the caller to retry shortly.
    /// </summary>
    public static HttpResponseMessage SecondaryRateLimited(int retryAfterSeconds = 0)
    {
        HttpResponseMessage response = new(HttpStatusCode.Forbidden);
        response.Headers.TryAddWithoutValidation("retry-after", retryAfterSeconds.ToString());
        return WithRateLimit(response, remaining: 3000);
    }

    /// <summary>
    /// Builds the <c>403</c> that GitHub sends when the hourly budget is spent, resetting after the given delay.
    /// </summary>
    public static HttpResponseMessage PrimaryRateLimited(TimeSpan resetsIn)
    {
        HttpResponseMessage response = new(HttpStatusCode.Forbidden);
        return WithRateLimit(response, remaining: 0, resetsIn);
    }

    /// <summary>
    /// Adds the <c>x-ratelimit-*</c> headers that GitHub reports its budget through.
    /// </summary>
    public static HttpResponseMessage WithRateLimit(HttpResponseMessage response, int remaining,
        TimeSpan? resetsIn = null, int limit = 5000)
    {
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow + (resetsIn ?? TimeSpan.FromMinutes(30));
        response.Headers.Remove("x-ratelimit-limit");
        response.Headers.Remove("x-ratelimit-remaining");
        response.Headers.Remove("x-ratelimit-reset");
        response.Headers.TryAddWithoutValidation("x-ratelimit-limit", limit.ToString());
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", remaining.ToString());
        response.Headers.TryAddWithoutValidation("x-ratelimit-reset", resetsAt.ToUnixTimeSeconds().ToString());
        return response;
    }
}
