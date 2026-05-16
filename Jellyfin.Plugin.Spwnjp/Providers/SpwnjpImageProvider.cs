using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Spwnjp.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Spwnjp.Providers;

/// <summary>
/// Supplies the backdrop image for a movie that's tagged with a spwn.jp event id.
/// Image bytes themselves are streamed by Jellyfin via <see cref="GetImageResponse"/> using
/// a plain <see cref="HttpClient"/> — image URLs are direct CDN GETs and don't need headless-shell.
/// </summary>
public class SpwnjpImageProvider : IRemoteImageProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SpwnjpImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpwnjpImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin-provided <see cref="IHttpClientFactory"/>.</param>
    /// <param name="logger">Logger.</param>
    public SpwnjpImageProvider(IHttpClientFactory httpClientFactory, ILogger<SpwnjpImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => SpwnjpConstants.DisplayName;

    /// <inheritdoc/>
    public bool Supports(BaseItem item) => item is Movie;

    /// <inheritdoc/>
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Backdrop };

    /// <inheritdoc/>
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.ProviderIds.TryGetValue(SpwnjpConstants.ProviderKey, out var eventId)
            || string.IsNullOrWhiteSpace(eventId))
        {
            return Enumerable.Empty<RemoteImageInfo>();
        }

        try
        {
            var fetcher = PageFetcherFactory.Create(_httpClientFactory);
            var client = new SpwnClient(fetcher);
            var ev = await client.GetEventAsync(eventId, cancellationToken).ConfigureAwait(false);

            if (ev.BackdropImageUrl is null)
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            return new[]
            {
                new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Backdrop,
                    Url = ev.BackdropImageUrl.ToString(),
                },
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Spwnjp image lookup failed for event {EventId}", eventId);
            return Enumerable.Empty<RemoteImageInfo>();
        }
    }

    /// <inheritdoc/>
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient();
        return http.GetAsync(new Uri(url), cancellationToken);
    }
}
