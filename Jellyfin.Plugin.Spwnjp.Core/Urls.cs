namespace Jellyfin.Plugin.Spwnjp.Core;

/// <summary>
/// Single source of truth for the spwn.jp URLs the plugin works with.
/// </summary>
public static class Urls
{
    /// <summary>
    /// Builds the canonical event-detail URL for a given spwn.jp event id (e.g. <c>evt_qB2HIXL5QZpwoeqcAKRD</c>).
    /// </summary>
    /// <param name="eventId">The spwn.jp event id.</param>
    /// <returns>The event-detail page URL.</returns>
    public static Uri EventUrl(string eventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        return new Uri($"https://spwn.jp/events/{Uri.EscapeDataString(eventId)}");
    }

    /// <summary>
    /// Builds the search URL for a given keyword.
    /// </summary>
    /// <param name="keyword">The search keyword.</param>
    /// <returns>The search-results page URL.</returns>
    public static Uri SearchUrl(string keyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        return new Uri($"https://spwn.jp/search?keyword={Uri.EscapeDataString(keyword)}");
    }
}
