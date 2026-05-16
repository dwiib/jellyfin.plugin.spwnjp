namespace Jellyfin.Plugin.Spwnjp.Providers;

/// <summary>
/// Constants shared across the three Jellyfin provider adapters.
/// </summary>
public static class SpwnjpConstants
{
    /// <summary>
    /// The provider-id key under which the spwn.jp event id is stored in
    /// <see cref="MediaBrowser.Model.Entities.IHasProviderIds.ProviderIds"/>. Must match
    /// the key used by <see cref="SpwnjpExternalId"/> so the UI field, the metadata
    /// provider, and the image provider all read/write the same dictionary slot.
    /// </summary>
    public const string ProviderKey = "Spwnjp";

    /// <summary>
    /// Display name shown next to results sourced from this provider.
    /// </summary>
    public const string DisplayName = "Spwnjp";
}
