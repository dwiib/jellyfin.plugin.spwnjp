using Jellyfin.Plugin.Spwnjp.Core.Model;
using Jellyfin.Plugin.Spwnjp.Core.Parsing;

namespace Jellyfin.Plugin.Spwnjp.Core;

/// <summary>
/// High-level facade: fetches spwn.jp pages via an <see cref="IPageFetcher"/> and parses them
/// into structured <see cref="SpwnEvent"/> / <see cref="SpwnSearchResult"/> values.
/// This is the surface the Jellyfin providers (and the CLI harness) consume.
/// </summary>
public sealed class SpwnClient
{
    private readonly IPageFetcher _fetcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpwnClient"/> class.
    /// </summary>
    /// <param name="fetcher">Page fetcher backend (direct HttpClient or headless-shell CDP).</param>
    public SpwnClient(IPageFetcher fetcher)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
        _fetcher = fetcher;
    }

    /// <summary>
    /// Looks up a spwn.jp event by its id and parses the detail page.
    /// </summary>
    /// <param name="eventId">The spwn.jp event id (e.g. <c>evt_qB2HIXL5QZpwoeqcAKRD</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed event.</returns>
    public async Task<SpwnEvent> GetEventAsync(string eventId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        var page = await _fetcher.FetchHtmlAsync(Urls.EventUrl(eventId), ct).ConfigureAwait(false);
        return EventPageParser.Parse(page.Html, eventId);
    }

    /// <summary>
    /// Runs a search against spwn.jp and parses the results page.
    /// </summary>
    /// <param name="keyword">The search keyword.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed search results.</returns>
    public async Task<IReadOnlyList<SpwnSearchResult>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var page = await _fetcher.FetchHtmlAsync(Urls.SearchUrl(keyword), ct).ConfigureAwait(false);
        return SearchPageParser.Parse(page.Html);
    }
}
