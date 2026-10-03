using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlaylistCovers.ScheduledTasks;

/// <summary>Dashboard task that (re)generates playlist covers.</summary>
public class GenerateCoversTask : IScheduledTask
{
    private readonly PlaylistCoverService _service;

    /// <summary>Initializes a new instance of the <see cref="GenerateCoversTask"/> class.</summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="providerManager">Provider manager.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public GenerateCoversTask(ILibraryManager libraryManager, IProviderManager providerManager, ILoggerFactory loggerFactory)
    {
        _service = new PlaylistCoverService(libraryManager, providerManager, loggerFactory.CreateLogger<PlaylistCoverService>());
    }

    /// <inheritdoc />
    public string Name => "Playlist-Cover erzeugen";

    /// <inheritdoc />
    public string Key => "PlaylistCoversGenerate";

    /// <inheritdoc />
    public string Description => "Rendert Cover für alle Playlists, deren Inhalt sich geändert hat.";

    /// <inheritdoc />
    public string Category => "Playlist Covers";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _service.RunAsync(progress, force: false, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks };
    }
}
