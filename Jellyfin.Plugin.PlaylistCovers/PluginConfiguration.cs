using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PlaylistCovers;

/// <summary>Plugin settings, stored by Jellyfin as XML.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets playlist name patterns (one per line, * and ? wildcards) that are never touched.</summary>
    public string ExcludePatterns { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional path to a .ttf/.otf file used for the title.</summary>
    public string FontPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether titles are rendered in upper case.</summary>
    public bool UpperCaseTitle { get; set; } = true;
}
