using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Spwnjp.Core.Parsing;

/// <summary>
/// Helpers for normalising spwn.jp CDN image URLs.
/// </summary>
public static partial class ImageUrl
{
    /// <summary>
    /// Strips the trailing <c>_WIDTHxHEIGHT</c> size suffix from a public-web.spwn.jp image URL
    /// to fetch the original resolution. Also tolerates a stray query string after the suffix.
    /// </summary>
    /// <param name="raw">URL as found in the page markup.</param>
    /// <returns>The original-resolution URL.</returns>
    public static Uri StripSizeSuffix(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        var trimmed = SizeSuffix().Replace(raw, string.Empty);
        return new Uri(trimmed);
    }

    [GeneratedRegex(@"_\d+x\d+(?=$|\?)")]
    private static partial Regex SizeSuffix();
}
