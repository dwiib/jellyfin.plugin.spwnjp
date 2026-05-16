namespace Jellyfin.Plugin.Spwnjp.Core;

/// <summary>
/// Which backend produced a fetch result.
/// </summary>
public enum PageFetchBackend
{
    /// <summary>Plain HttpClient GET. Whatever spwn.jp's edge returns directly.</summary>
    Direct,

    /// <summary>Page rendered inside headless-shell and dumped via CDP Runtime.evaluate.</summary>
    Cdp,
}

/// <summary>
/// Result of a single page fetch.
/// </summary>
/// <param name="Html">The HTML payload as returned by the chosen backend.</param>
/// <param name="RequestedUrl">The URL the caller asked for.</param>
/// <param name="Backend">Which backend served the fetch.</param>
/// <param name="Elapsed">Wall-clock elapsed time for the fetch.</param>
public sealed record PageFetchResult(string Html, Uri RequestedUrl, PageFetchBackend Backend, TimeSpan Elapsed);

/// <summary>
/// Abstraction over "give me the HTML for this URL." Two implementations: direct HttpClient and CDP via headless-shell.
/// The metadata provider depends on this; the CLI harness uses it the same way Jellyfin's providers will.
/// </summary>
public interface IPageFetcher
{
    /// <summary>
    /// Fetches the HTML for <paramref name="url"/>.
    /// </summary>
    /// <param name="url">The page URL.</param>
    /// <param name="readyExpression">
    /// Optional JavaScript expression that evaluates to <c>true</c> once the page has hydrated
    /// enough to scrape. Used by the CDP backend to poll instead of sleeping; ignored by direct HTTP.
    /// Pass <c>null</c> when fetching arbitrary URLs without a known readiness marker — the CDP
    /// backend then falls back to a short fixed delay.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fetch result.</returns>
    Task<PageFetchResult> FetchHtmlAsync(Uri url, string? readyExpression = null, CancellationToken ct = default);
}
