using System.Globalization;
using System.Net.Http.Headers;

namespace PackageGuard.Core.GitHub;

/// <summary>
/// Tracks the primary rate limit budget reported by the GitHub API through its <c>x-ratelimit-*</c> response headers.
/// </summary>
internal sealed class GitHubRateLimit
{
    /// <summary>
    /// Guards concurrent reads and writes of the mutable budget fields.
    /// </summary>
    private readonly Lock stateLock = new();

    private int? limit;
    private int? remaining;
    private DateTimeOffset? resetsAt;

    /// <summary>
    /// The number of requests still available in the current rate limit window, or <see langword="null"/> when the
    /// API has not reported a budget yet.
    /// </summary>
    public int? Remaining
    {
        get
        {
            lock (stateLock)
            {
                return remaining;
            }
        }
    }

    /// <summary>
    /// The moment at which the current rate limit window resets, or <see langword="null"/> when unknown.
    /// </summary>
    private DateTimeOffset? ResetsAt
    {
        get
        {
            lock (stateLock)
            {
                return resetsAt;
            }
        }
    }

    /// <summary>
    /// The number of requests held back for essential calls. Optional calls are skipped once the remaining budget
    /// drops to this level, so that the signals that matter most still get through.
    /// </summary>
    private int Reserve
    {
        get
        {
            lock (stateLock)
            {
                return limit is null ? DefaultReserve : Math.Max(DefaultReserve, limit.Value / 20);
            }
        }
    }

    /// <summary>
    /// The reserve applied when the API has not reported a rate limit ceiling yet.
    /// </summary>
    private const int DefaultReserve = 10;

    /// <summary>
    /// Records the rate limit budget advertised by a GitHub API response.
    /// </summary>
    /// <param name="headers">The response headers to read the <c>x-ratelimit-*</c> values from.</param>
    public void Update(HttpResponseHeaders headers)
    {
        lock (stateLock)
        {
            limit = TryReadInt(headers, "x-ratelimit-limit") ?? limit;
            remaining = TryReadInt(headers, "x-ratelimit-remaining") ?? remaining;
            resetsAt = TryReadReset(headers) ?? resetsAt;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the remaining budget has dropped into the reserve, meaning optional
    /// requests should be skipped.
    /// </summary>
    public bool IsInReserve()
    {
        lock (stateLock)
        {
            return remaining is not null && remaining.Value <= Reserve;
        }
    }

    /// <summary>
    /// Returns how long to wait before the rate limit window resets, or <see langword="null"/> when the reset time
    /// is unknown or already in the past.
    /// </summary>
    public TimeSpan? TimeUntilReset(DateTimeOffset now)
    {
        DateTimeOffset? reset = ResetsAt;
        if (reset is null || reset.Value <= now)
        {
            return null;
        }

        return reset.Value - now;
    }

    /// <summary>
    /// Reads a single integer header value, returning <see langword="null"/> when it is absent or malformed.
    /// </summary>
    private static int? TryReadInt(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            return null;
        }

        string? raw = values.FirstOrDefault();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
    }

    /// <summary>
    /// Reads the <c>x-ratelimit-reset</c> header, which carries the reset moment as Unix seconds.
    /// </summary>
    private static DateTimeOffset? TryReadReset(HttpResponseHeaders headers)
    {
        int? epochSeconds = TryReadInt(headers, "x-ratelimit-reset");
        return epochSeconds is null ? null : DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value);
    }
}
