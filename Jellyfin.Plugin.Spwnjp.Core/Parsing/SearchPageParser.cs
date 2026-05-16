using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Jellyfin.Plugin.Spwnjp.Core.Model;

namespace Jellyfin.Plugin.Spwnjp.Core.Parsing;

/// <summary>
/// Parses a search-results page (<c>https://spwn.jp/search?keyword=&lt;kw&gt;</c>) into
/// a list of <see cref="SpwnSearchResult"/>.
/// </summary>
public static partial class SearchPageParser
{
    /// <summary>
    /// Parses the rendered HTML of a search-results page.
    /// </summary>
    /// <param name="html">The post-render HTML.</param>
    /// <returns>The parsed search results, in page order.</returns>
    public static IReadOnlyList<SpwnSearchResult> Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        // Each result is an <a href="/events/evt_..."> wrapping an <article>.
        // Anchoring on the href pattern is more robust than the (emotion-generated)
        // class names that wrap them.
        var anchors = root.SelectNodes("//a[starts-with(@href, '/events/evt_')]");
        if (anchors is null || anchors.Count == 0)
        {
            return Array.Empty<SpwnSearchResult>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<SpwnSearchResult>();
        foreach (var anchor in anchors)
        {
            var result = ParseAnchor(anchor);
            if (result is null || !seen.Add(result.EventId))
            {
                continue;
            }

            results.Add(result);
        }

        return results;
    }

    private static SpwnSearchResult? ParseAnchor(HtmlNode anchor)
    {
        var href = anchor.GetAttributeValue("href", string.Empty);
        var eventId = ExtractEventId(href);
        if (eventId is null)
        {
            return null;
        }

        var title = HtmlEntity.DeEntitize(
            anchor.SelectSingleNode(".//h3")?.InnerText.Trim() ?? string.Empty);
        if (string.IsNullOrEmpty(title))
        {
            // Fallback: the result-card image's alt also mirrors the title.
            title = anchor.SelectSingleNode(".//img")?.GetAttributeValue("alt", string.Empty) ?? string.Empty;
            title = HtmlEntity.DeEntitize(title).Trim();
        }

        var performerLine = HtmlEntity.DeEntitize(
            anchor.SelectSingleNode(".//footer")?.InnerText.Trim() ?? string.Empty);
        if (string.IsNullOrEmpty(performerLine))
        {
            performerLine = null;
        }

        DateOnly? date = null;
        var dateAttr = anchor.SelectSingleNode(".//time[@datetime]")?.GetAttributeValue("datetime", null);
        if (!string.IsNullOrEmpty(dateAttr)
            && DateOnly.TryParseExact(dateAttr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
        }

        Uri? image = null;
        var imgSrc = anchor.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty);
        if (!string.IsNullOrWhiteSpace(imgSrc))
        {
            // Search-result cards have URLs with a stray double slash —
            // public-web.spwn.jp//events/... — normalise it before parsing.
            var normalised = DoubleSlash().Replace(imgSrc!, "/events/");
            image = ImageUrl.StripSizeSuffix(normalised);
        }

        return new SpwnSearchResult(eventId, title, performerLine, date, image);
    }

    private static string? ExtractEventId(string href)
    {
        if (string.IsNullOrEmpty(href))
        {
            return null;
        }

        var match = EventIdRegex().Match(href);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"evt_[A-Za-z0-9]+")]
    private static partial Regex EventIdRegex();

    [GeneratedRegex(@"//events/")]
    private static partial Regex DoubleSlash();
}
