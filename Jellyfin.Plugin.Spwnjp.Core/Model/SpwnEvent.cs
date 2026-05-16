namespace Jellyfin.Plugin.Spwnjp.Core.Model;

/// <summary>
/// A spwn.jp event scraped from <c>https://spwn.jp/events/&lt;event-id&gt;</c>.
/// </summary>
/// <param name="EventId">The spwn.jp event id (e.g. <c>evt_qB2HIXL5QZpwoeqcAKRD</c>).</param>
/// <param name="Title">Event title (also appears as the backdrop image's alt text).</param>
/// <param name="PerformerLine">The performer list as displayed on the page, separators preserved verbatim.</param>
/// <param name="Performers">Individual performer names, sourced from the structured <c>#act_info</c> section.</param>
/// <param name="Overview">Event description / overview text.</param>
/// <param name="Date">Event date if it could be parsed, otherwise null.</param>
/// <param name="BackdropImageUrl">Full-resolution backdrop URL (size suffix stripped), or null if no backdrop image was found.</param>
public sealed record SpwnEvent(
    string EventId,
    string Title,
    string? PerformerLine,
    IReadOnlyList<string> Performers,
    string? Overview,
    DateOnly? Date,
    Uri? BackdropImageUrl);
