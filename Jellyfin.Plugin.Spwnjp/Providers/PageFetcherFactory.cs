using System;
using System.Net.Http;
using Jellyfin.Plugin.Spwnjp.Core;

namespace Jellyfin.Plugin.Spwnjp.Providers;

/// <summary>
/// Constructs the correct <see cref="IPageFetcher"/> for the current plugin configuration:
/// <see cref="CdpPageFetcher"/> if a headless-shell URL is configured, otherwise
/// <see cref="DirectPageFetcher"/>.
/// </summary>
internal static class PageFetcherFactory
{
    /// <summary>
    /// Creates a fetcher backed by an <see cref="HttpClient"/> from <paramref name="httpClientFactory"/>.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's <see cref="IHttpClientFactory"/>.</param>
    /// <returns>A page fetcher.</returns>
    public static IPageFetcher Create(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        var http = httpClientFactory.CreateClient();
        var headless = Plugin.Instance?.Configuration.HeadlessShellUrl;
        if (!string.IsNullOrWhiteSpace(headless)
            && Uri.TryCreate(headless, UriKind.Absolute, out var url))
        {
            return new CdpPageFetcher(http, url);
        }

        return new DirectPageFetcher(http);
    }
}
