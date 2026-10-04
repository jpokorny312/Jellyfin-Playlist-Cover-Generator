using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PlaylistCovers;

/// <summary>Plugin settings, stored by Jellyfin as XML.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets playlist name patterns (one per line, * and ? wildcards) that are never touched.</summary>
    public string ExcludePatterns { get; set; } = string.Empty;

    /// <summary>Gets or sets the cover style: Auto (default), Wall, Mosaic or Hero.</summary>
    public string Style { get; set; } = "Auto";
}
