using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Jellyfin.Plugin.Spwnjp.Core.Model;

namespace Jellyfin.Plugin.Spwnjp.Core.Parsing;

/// <summary>
/// Parses an event-detail page (<c>https://spwn.jp/events/&lt;event-id&gt;</c>) into a <see cref="SpwnEvent"/>.
/// CSS class names on spwn.jp are emotion-generated and not stable; this parser anchors on structure
/// (id="act_info", the breadcrumb shape, the public-web.spwn.jp image URL pattern, etc.).
/// </summary>
public static partial class EventPageParser
{
    /// <summary>
    /// Parses the rendered HTML of an event-detail page.
    /// </summary>
    /// <param name="html">The post-render HTML.</param>
    /// <param name="eventId">The spwn.jp event id whose page is being parsed; preserved on the result.</param>
    /// <returns>The parsed event.</returns>
    public static SpwnEvent Parse(string html, string eventId)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var (title, backdrop) = ExtractTitleAndBackdrop(root);
        var performerLine = ExtractPerformerLine(root);
        var performers = ExtractPerformers(root);
        var overview = ExtractOverview(root);
        var date = ExtractDate(html);

        return new SpwnEvent(
            EventId: eventId,
            Title: title,
            PerformerLine: performerLine,
            Performers: performers,
            Overview: overview,
            Date: date,
            BackdropImageUrl: backdrop);
    }

    private static (string Title, Uri? Backdrop) ExtractTitleAndBackdrop(HtmlNode root)
    {
        // The hero backdrop image is the first <img> whose src points at the
        // event-images CDN bucket. Its alt attribute mirrors the event title.
        var img = root.SelectSingleNode(
            "//img[starts-with(@src, 'https://public-web.spwn.jp/events/')]");
        var alt = img?.GetAttributeValue("alt", string.Empty);
        var src = img?.GetAttributeValue("src", string.Empty);

        // Fallback to the breadcrumb's last <li> if the image is missing.
        if (string.IsNullOrWhiteSpace(alt))
        {
            var breadcrumbLi = root.SelectSingleNode(
                "//ul[li/a[@href='/events']]/li[not(a)][1]");
            alt = HtmlEntity.DeEntitize(breadcrumbLi?.InnerText.Trim() ?? string.Empty);
        }

        Uri? backdrop = null;
        if (!string.IsNullOrWhiteSpace(src))
        {
            backdrop = ImageUrl.StripSizeSuffix(src);
        }

        return (alt ?? string.Empty, backdrop);
    }

    private static string? ExtractPerformerLine(HtmlNode root)
    {
        // The performer list <p> sits directly after the event-title <h2 translate="no">.
        var p = root.SelectSingleNode(
            "//h2[@translate='no']/following-sibling::p[1]");
        if (p is null)
        {
            return null;
        }

        var text = HtmlEntity.DeEntitize(p.InnerText).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static IReadOnlyList<string> ExtractPerformers(HtmlNode root)
    {
        // The #act_info section contains one <article> per cast member with the
        // performer name in an <h3>. This is the most reliable source of
        // individual names because it doesn't depend on a separator character.
        var nodes = root.SelectNodes("//*[@id='act_info']//article//h3");
        if (nodes is null || nodes.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(nodes.Count);
        foreach (var node in nodes)
        {
            var name = HtmlEntity.DeEntitize(node.InnerText).Trim();
            if (!string.IsNullOrEmpty(name))
            {
                result.Add(name);
            }
        }

        return result;
    }

    private static string? ExtractOverview(HtmlNode root)
    {
        // The description block carries a non-emotion class name we can depend on.
        var div = root.SelectSingleNode(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' translate-this-block ')]");
        if (div is null)
        {
            return null;
        }

        var text = ConvertToPlainText(div);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static DateOnly? ExtractDate(string html)
    {
        // Event-detail pages format the date as YYYY/MM/DD. Take the first hit;
        // false positives are unlikely against that specific shape.
        var match = DateRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                match.Value,
                "yyyy/MM/dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        return null;
    }

    private static string ConvertToPlainText(HtmlNode node)
    {
        // Walk the element tree producing a plain-text rendering: <br> and <hr>
        // become newlines, text nodes are de-entitized, everything else
        // contributes its child content.
        var sb = new StringBuilder();
        Walk(node, sb);
        var lines = sb.ToString()
            .Split('\n')
            .Select(static l => l.TrimEnd())
            .ToArray();
        return string.Join('\n', lines).Trim('\n', ' ', '\t');
    }

    private static void Walk(HtmlNode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child.NodeType)
            {
                case HtmlNodeType.Text:
                    sb.Append(HtmlEntity.DeEntitize(child.InnerText));
                    break;
                case HtmlNodeType.Element when child.Name.Equals("br", StringComparison.OrdinalIgnoreCase):
                    sb.Append('\n');
                    break;
                case HtmlNodeType.Element when child.Name.Equals("hr", StringComparison.OrdinalIgnoreCase):
                    sb.Append("\n\n");
                    break;
                case HtmlNodeType.Element:
                    Walk(child, sb);
                    break;
                default:
                    break;
            }
        }
    }

    [GeneratedRegex(@"\d{4}/\d{2}/\d{2}")]
    private static partial Regex DateRegex();
}
