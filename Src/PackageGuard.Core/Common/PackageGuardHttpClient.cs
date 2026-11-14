using System.Net.Http.Headers;

namespace PackageGuard.Core.Common;

/// <summary>
/// Creates the <see cref="HttpClient"/> instances PackageGuard uses to talk to registries and APIs, so that settings
/// shared by all of them (such as the User-Agent) live in one place.
/// </summary>
internal static class PackageGuardHttpClient
{
    /// <summary>
    /// Creates a client that identifies itself as PackageGuard.
    /// </summary>
    /// <param name="timeout">The request timeout, or <see langword="null"/> for the .NET default of 100 seconds.</param>
    /// <param name="handler">An optional message handler, used by the tests to avoid real network traffic.</param>
    public static HttpClient Create(TimeSpan? timeout = null, HttpMessageHandler? handler = null)
    {
        HttpClient client = handler is null ? new HttpClient() : new HttpClient(handler);
        if (timeout is not null)
        {
            client.Timeout = timeout.Value;
        }

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PackageGuard", "v1"));
        return client;
    }
}
