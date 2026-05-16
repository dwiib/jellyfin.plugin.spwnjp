using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Spwnjp.Providers;

/// <summary>
/// Registers the spwn.jp event id as a known external id for movie items, which surfaces a
/// "Spwn event ID" field on the metadata edit page in the Jellyfin web UI.
/// </summary>
public class SpwnjpExternalId : IExternalId
{
    /// <inheritdoc/>
    public string ProviderName => SpwnjpConstants.DisplayName;

    /// <inheritdoc/>
    public string Key => SpwnjpConstants.ProviderKey;

    /// <inheritdoc/>
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc/>
    public string UrlFormatString => "https://spwn.jp/events/{0}";

    /// <inheritdoc/>
    public bool Supports(IHasProviderIds item) => item is Movie;
}
