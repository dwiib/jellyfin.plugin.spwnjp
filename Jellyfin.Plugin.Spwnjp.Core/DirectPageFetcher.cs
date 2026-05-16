using System.Diagnostics;
using System.Net.Http;

namespace Jellyfin.Plugin.Spwnjp.Core;

/// <summary>
/// Fetches pages with a plain <see cref="HttpClient"/>. Returns whatever spwn.jp's edge serves directly,
/// which on a Firebase-backed SPA is typically just the bootstrap shell.
/// </summary>
public sealed class DirectPageFetcher : IPageFetcher
{
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectPageFetcher"/> class.
    /// </summary>
    /// <param name="http">An <see cref="HttpClient"/> instance. The CLI passes its own; Jellyfin will pass one from <c>IHttpClientFactory</c>.</param>
    public DirectPageFetcher(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The <paramref name="readyExpression"/> parameter is ignored — a direct HTTP fetch
    /// returns whatever the server emits in a single response, with no opportunity to wait
    /// for client-side rendering.
    /// </remarks>
    public async Task<PageFetchResult> FetchHtmlAsync(Uri url, string? readyExpression = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        _ = readyExpression;
        var sw = Stopwatch.StartNew();
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        sw.Stop();
        return new PageFetchResult(html, url, PageFetchBackend.Direct, sw.Elapsed);
    }
}
