using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

using Jellyfin.Plugin.Kanon.Configuration;
using Jellyfin.Plugin.Kanon.Helpers;
using Jellyfin.Plugin.Kanon.Segments;

namespace Jellyfin.Plugin.Kanon.Tasks;

/// <summary>
/// Scheduled task that writes the whole-episode skip segments for every tagged anime episode,
/// following the segment types chosen in the plugin settings.
/// </summary>
public class FillerSegmentTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSegmentManager _segmentManager;
    private readonly ILogger<FillerSegmentTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FillerSegmentTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    /// <param name="segmentManager">Jellyfin's media segment manager.</param>
    /// <param name="logger">Logging instance for diagnostic output.</param>
    public FillerSegmentTask(ILibraryManager libraryManager, IMediaSegmentManager segmentManager, ILogger<FillerSegmentTask> logger)
    {
        _libraryManager = libraryManager;
        _segmentManager = segmentManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Update Filler Skip Segments";
    /// <inheritdoc />
    public string Key => "KanonFillerSegmentTask";
    /// <inheritdoc />
    public string Description => "Marks filler (and optionally mixed or anime-canon) episodes with a whole-episode media segment, so Media Segment Actions can skip them. Run after the canon/filler tag update or after changing the skip settings.";
    /// <inheritdoc />
    public string Category => "Kanon";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var seriesList = CollectAnimeSeries.Execute(_libraryManager);
        double done = 0;
        int marked = 0;

        foreach (var show in seriesList)
        {
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Parent = show,
                IncludeItemTypes = [ BaseItemKind.Episode ],
                Recursive = true,
                IsVirtualItem = false
            })
            .Cast<Episode>()
            .ToList();

            foreach (var episode in episodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await FillerSegments.SyncAsync(episode, _segmentManager, _libraryManager, config).ConfigureAwait(false))
                {
                    marked++;
                }
            }

            done++;
            progress?.Report(done / seriesList.Count * 100);
        }

        _logger.LogInformation("Kanon: {Marked} episodes carry a filler skip segment.", marked);
        progress?.Report(100);
    }
}
