namespace Jellyfin.Plugin.Spwnjp.Core.Model;

/// <summary>
/// A single search result from <c>https://spwn.jp/search?keyword=&lt;kw&gt;</c>.
/// </summary>
/// <param name="EventId">The spwn.jp event id, extracted from the result's link.</param>
/// <param name="Title">Event title as shown in the result list.</param>
/// <param name="PerformerLine">Performer list as displayed in the result footer, may be empty.</param>
/// <param name="Date">Event date if present on the result card, otherwise null.</param>
/// <param name="BackdropImageUrl">Result-card image URL (full-resolution, size suffix stripped), or null.</param>
public sealed record SpwnSearchResult(
    string EventId,
    string Title,
    string? PerformerLine,
    DateOnly? Date,
    Uri? BackdropImageUrl);
