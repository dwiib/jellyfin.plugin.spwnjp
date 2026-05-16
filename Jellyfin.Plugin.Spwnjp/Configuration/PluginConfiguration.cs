using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Spwnjp.Configuration;

/// <summary>
/// Persisted settings for the Spwnjp plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        HeadlessShellUrl = string.Empty;
    }

    /// <summary>
    /// Gets or sets the optional base URL of a headless-shell instance used to render
    /// spwn.jp pages before scraping. When empty, the plugin falls back to direct
    /// HTTP fetches. Example: <c>http://localhost:9222</c>.
    /// </summary>
    public string HeadlessShellUrl { get; set; }
}
