using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Spwnjp.Core;
using Jellyfin.Plugin.Spwnjp.Core.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Spwnjp.Providers;

/// <summary>
/// The Jellyfin-facing metadata provider for spwn.jp events. Two entry points:
/// <list type="bullet">
///   <item><see cref="GetSearchResults"/> — fired by the Identify dialog or automatic match. If the user
///   has pinned a spwn event id via the external-id field, returns that single result; otherwise runs a
///   keyword search against spwn.jp/search.</item>
///   <item><see cref="GetMetadata"/> — once a result is chosen (which carries the event id on its
///   <c>ProviderIds</c>), fetches and parses the event-detail page and populates the
///   <see cref="MetadataResult{Movie}"/>.</item>
/// </list>
/// </summary>
public class SpwnjpMetadataProvider : IRemoteMetadataProvider<Movie, MovieInfo>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SpwnjpMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpwnjpMetadataProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin-provided <see cref="IHttpClientFactory"/>.</param>
    /// <param name="logger">Logger.</param>
    public SpwnjpMetadataProvider(IHttpClientFactory httpClientFactory, ILogger<SpwnjpMetadataProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => SpwnjpConstants.DisplayName;

    /// <inheritdoc/>
    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        var result = new MetadataResult<Movie>();

        if (!info.ProviderIds.TryGetValue(SpwnjpConstants.ProviderKey, out var eventId)
            || string.IsNullOrWhiteSpace(eventId))
        {
            return result;
        }

        try
        {
            var fetcher = PageFetcherFactory.Create(_httpClientFactory);
            var client = new SpwnClient(fetcher);
            var ev = await client.GetEventAsync(eventId, cancellationToken).ConfigureAwait(false);
            PopulateResult(result, ev);
            result.HasMetadata = true;
            result.QueriedById = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Spwnjp metadata lookup failed for event {EventId}", eventId);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(searchInfo);

        var fetcher = PageFetcherFactory.Create(_httpClientFactory);
        var client = new SpwnClient(fetcher);

        // Pinned event id takes precedence: skip the search and fetch the detail page directly.
        if (searchInfo.ProviderIds.TryGetValue(SpwnjpConstants.ProviderKey, out var pinnedId)
            && !string.IsNullOrWhiteSpace(pinnedId))
        {
            try
            {
                var ev = await client.GetEventAsync(pinnedId, cancellationToken).ConfigureAwait(false);
                return new[] { ToRemoteSearchResult(ev) };
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Spwnjp pinned-id lookup failed for {EventId}", pinnedId);
                return Enumerable.Empty<RemoteSearchResult>();
            }
        }

        var keyword = searchInfo.Name;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Enumerable.Empty<RemoteSearchResult>();
        }

        try
        {
            var results = await client.SearchAsync(keyword, cancellationToken).ConfigureAwait(false);
            return results.Select(ToRemoteSearchResult);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Spwnjp search failed for keyword {Keyword}", keyword);
            return Enumerable.Empty<RemoteSearchResult>();
        }
    }

    /// <inheritdoc/>
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient();
        return http.GetAsync(new Uri(url), cancellationToken);
    }

    private static void PopulateResult(MetadataResult<Movie> result, SpwnEvent ev)
    {
        var movie = new Movie
        {
            Name = ev.Title,
            Tagline = ev.PerformerLine,
            Overview = ev.Overview,
        };
        movie.ProviderIds[SpwnjpConstants.ProviderKey] = ev.EventId;

        if (ev.Date is { } date)
        {
            var dt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            movie.PremiereDate = dt;
            movie.ProductionYear = date.Year;
        }

        result.Item = movie;

        foreach (var name in ev.Performers)
        {
            result.AddPerson(new PersonInfo
            {
                Name = name,
                Type = PersonKind.Actor,
            });
        }
    }

    private static RemoteSearchResult ToRemoteSearchResult(SpwnEvent ev)
    {
        var r = new RemoteSearchResult
        {
            Name = ev.Title,
            Overview = ev.Overview,
            ImageUrl = ev.BackdropImageUrl?.ToString(),
            SearchProviderName = SpwnjpConstants.DisplayName,
        };
        r.ProviderIds[SpwnjpConstants.ProviderKey] = ev.EventId;
        if (ev.Date is { } date)
        {
            r.PremiereDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            r.ProductionYear = date.Year;
        }

        return r;
    }

    private static RemoteSearchResult ToRemoteSearchResult(SpwnSearchResult sr)
    {
        var r = new RemoteSearchResult
        {
            Name = sr.Title,
            ImageUrl = sr.BackdropImageUrl?.ToString(),
            SearchProviderName = SpwnjpConstants.DisplayName,
        };
        r.ProviderIds[SpwnjpConstants.ProviderKey] = sr.EventId;
        if (sr.Date is { } date)
        {
            r.PremiereDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            r.ProductionYear = date.Year;
        }

        return r;
    }
}
