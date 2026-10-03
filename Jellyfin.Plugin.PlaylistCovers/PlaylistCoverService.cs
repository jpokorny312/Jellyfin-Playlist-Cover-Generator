using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.PlaylistCovers;

/// <summary>Collects artwork for every playlist and stores a rendered cover as its primary image.</summary>
public class PlaylistCoverService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<PlaylistCoverService> _logger;

    /// <summary>Initializes a new instance of the <see cref="PlaylistCoverService"/> class.</summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="providerManager">Provider manager.</param>
    /// <param name="logger">Logger.</param>
    public PlaylistCoverService(ILibraryManager libraryManager, IProviderManager providerManager, ILogger<PlaylistCoverService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <summary>Generates covers for all playlists whose content changed since the last run.</summary>
    /// <param name="progress">Progress callback (0-100).</param>
    /// <param name="force">Re-render even if nothing changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task RunAsync(IProgress<double> progress, bool force, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var excludes = ParsePatterns(config.ExcludePatterns);
        var state = LoadState();

        var playlists = _libraryManager
            .GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Playlist }, Recursive = true })
            .OfType<Playlist>()
            .ToList();

        _logger.LogInformation("Playlist Covers: {Count} playlists found", playlists.Count);

        for (var i = 0; i < playlists.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playlist = playlists[i];
            try
            {
                await ProcessAsync(playlist, config, excludes, state, force, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Playlist Covers: failed for playlist {Name}", playlist.Name);
            }

            progress.Report((i + 1) * 100.0 / playlists.Count);
        }

        SaveState(state);
    }

    private async Task ProcessAsync(
        Playlist playlist,
        PluginConfiguration config,
        List<Regex> excludes,
        Dictionary<string, string> state,
        bool force,
        CancellationToken ct)
    {
        if (excludes.Any(r => r.IsMatch(playlist.Name)))
        {
            _logger.LogDebug("Playlist Covers: {Name} excluded", playlist.Name);
            return;
        }

        var children = playlist.GetLinkedChildren().ToList();
        if (children.Count == 0)
        {
            return;
        }

        var usePoster = !string.Equals(config.Layout, "Landscape", StringComparison.OrdinalIgnoreCase);
        var mainPaths = usePoster ? PickPosters(children, 4) : PickBackdrops(children, 3);
        var logoPaths = usePoster ? new List<string>() : PickLogos(children, 3);
        var movies = children.Count(c => c is Movie);
        var series = children.Count(c => c is Series or Season or Episode);

        var signature = Signature(playlist.Name, children, mainPaths, logoPaths, config, usePoster);
        var key = playlist.Id.ToString("N");

        // Also compare the image currently attached to the playlist: if something else
        // (e.g. Jellyfin's own collage generator) replaced our cover, render it again.
        if (!force && state.TryGetValue(key, out var old) && old == signature + "#" + ImageMarker(playlist))
        {
            return;
        }

        var images = Load(mainPaths);
        var logos = Load(logoPaths);
        try
        {
            var jpeg = usePoster
                ? CoverRenderer.RenderPosterJpeg(playlist.Name, images, movies, series, config.FontPath)
                : CoverRenderer.RenderJpeg(playlist.Name, images, logos, movies, series, config.FontPath, config.UpperCaseTitle);
            using var stream = new MemoryStream(jpeg);
            await _providerManager
                .SaveImage(playlist, stream, "image/jpeg", ImageType.Primary, null, ct)
                .ConfigureAwait(false);
            await playlist.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ct).ConfigureAwait(false);
            state[key] = signature + "#" + ImageMarker(playlist);
            _logger.LogInformation("Playlist Covers: cover updated for {Name} ({Layout})", playlist.Name, usePoster ? "poster" : "landscape");
        }
        finally
        {
            images.ForEach(b => b.Dispose());
            logos.ForEach(b => b.Dispose());
        }
    }

    private static string ImageMarker(BaseItem item)
    {
        var info = item.GetImageInfo(ImageType.Primary, 0);
        return info == null ? string.Empty : $"{info.Path}|{info.DateModified.Ticks}";
    }

    /// <summary>Posters of the titles; episodes and seasons use their series poster.</summary>
    private static List<string> PickPosters(List<BaseItem> items, int max)
    {
        var all = items.Select(FindPoster).Where(p => p != null).Select(p => p!).Distinct().ToList();
        return Spread(all, max);
    }

    private static string? FindPoster(BaseItem item)
    {
        var target = item;
        if (item is Episode episode && episode.Series != null)
        {
            target = episode.Series;
        }
        else if (item is Season season && season.Series != null)
        {
            target = season.Series;
        }

        var info = target.GetImageInfo(ImageType.Primary, 0);
        return info != null && !string.IsNullOrEmpty(info.Path) && File.Exists(info.Path) ? info.Path : null;
    }

    /// <summary>Spreads the picks across the playlist so the cover is not just the first three titles.</summary>
    private static List<string> PickBackdrops(List<BaseItem> items, int max)
    {
        var all = items.Select(i => FindImage(i, ImageType.Backdrop)).Where(p => p != null).Select(p => p!).Distinct().ToList();
        return Spread(all, max);
    }

    private static List<string> PickLogos(List<BaseItem> items, int max) =>
        items.Select(i => FindImage(i, ImageType.Logo)).Where(p => p != null).Select(p => p!).Distinct().Take(max).ToList();

    private static List<string> Spread(List<string> all, int max)
    {
        if (all.Count <= max)
        {
            return all;
        }

        return Enumerable.Range(0, max).Select(i => all[i * (all.Count - 1) / (max - 1)]).ToList();
    }

    /// <summary>Image of the item itself, or of its season/series (episodes usually have none).</summary>
    private static string? FindImage(BaseItem item, ImageType type)
    {
        BaseItem? current = item;
        for (var depth = 0; current != null && depth < 4; depth++)
        {
            var info = current.GetImageInfo(type, 0);
            if (info != null && !string.IsNullOrEmpty(info.Path) && File.Exists(info.Path))
            {
                return info.Path;
            }

            current = current.GetParent();
        }

        return null;
    }

    private List<SKBitmap> Load(IEnumerable<string> paths)
    {
        var result = new List<SKBitmap>();
        foreach (var p in paths)
        {
            try
            {
                var bmp = SKBitmap.Decode(p);
                if (bmp != null)
                {
                    result.Add(bmp);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Playlist Covers: cannot decode {Path}", p);
            }
        }

        return result;
    }

    private static string Signature(string name, List<BaseItem> children, List<string> images, List<string> logos, PluginConfiguration cfg, bool poster)
    {
        var sb = new StringBuilder();
        sb.Append("v2|").Append(poster ? "poster" : "landscape").Append('|').Append(name).Append('|').Append(cfg.FontPath);
        if (!poster)
        {
            sb.Append('|').Append(cfg.UpperCaseTitle);
        }
        foreach (var c in children.Take(200))
        {
            sb.Append('|').Append(c.Id.ToString("N"));
        }

        foreach (var p in images.Concat(logos))
        {
            sb.Append('|').Append(p).Append(':').Append(File.GetLastWriteTimeUtc(p).Ticks);
        }

        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static List<Regex> ParsePatterns(string raw) =>
        raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();

    private static string StatePath => Path.Combine(Plugin.Instance?.DataFolderPath ?? Path.GetTempPath(), "cover-state.json");

    private Dictionary<string, string> LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StatePath)) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playlist Covers: state file unreadable, starting fresh");
        }

        return new Dictionary<string, string>();
    }

    private void SaveState(Dictionary<string, string> state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playlist Covers: cannot write state file");
        }
    }
}
