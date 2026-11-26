using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

using Jellyfin.Plugin.Kanon.Configuration;
using Jellyfin.Plugin.Kanon.Helpers;

namespace Jellyfin.Plugin.Kanon.Tasks;

/// <summary>
/// Scheduled task that builds one public playlist per anime series holding every episode
/// except those tagged Filler (and, when configured, Mixed Canon/Filler), so playing the playlist skips filler.
/// </summary>
public class CanonPlaylistTask : IScheduledTask
{
    private const string FillerTag = "Filler";
    private const string MixedTag = "Mixed Canon/Filler";
    private const string PlaylistSuffix = " (No Filler)";

    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<CanonPlaylistTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CanonPlaylistTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Service for accessing library items.</param>
    /// <param name="playlistManager">Service for creating playlists.</param>
    /// <param name="userManager">Service for finding the playlist owner.</param>
    /// <param name="logger">Logging instance for diagnostic output.</param>
    public CanonPlaylistTask(ILibraryManager libraryManager, IPlaylistManager playlistManager, IUserManager userManager, ILogger<CanonPlaylistTask> logger)
    {
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Build No-Filler Playlists";
    /// <inheritdoc />
    public string Key => "KanonCanonPlaylistTask";
    /// <inheritdoc />
    public string Description => "Creates a public playlist for each tagged anime series with every episode except filler. Run it after the canon/filler tag update.";
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
        var owner = _userManager.GetUsers().FirstOrDefault(u => u.HasPermission(Jellyfin.Database.Implementations.Enums.PermissionKind.IsAdministrator));
        if (owner == null)
        {
            _logger.LogWarning("Kanon: no administrator found to own the no-filler playlists.");
            return;
        }

        var seriesList = CollectAnimeSeries.Execute(_libraryManager);
        double done = 0;

        foreach (var show in seriesList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Parent = show,
                IncludeItemTypes = [ BaseItemKind.Episode ],
                Recursive = true,
                IsVirtualItem = false
            })
            .Cast<Episode>()
            .Where(e => e.IndexNumber.HasValue && (e.ParentIndexNumber ?? 1) > 0)
            .OrderBy(e => e.ParentIndexNumber ?? 1)
            .ThenBy(e => e.IndexNumber)
            .ToList();

            // Only series the tag update has classified get a playlist; otherwise it would just copy the show.
            bool classified = episodes.Any(e => e.Tags != null && e.Tags.Contains(FillerTag));
            if (classified)
            {
                await ReplacePlaylistAsync(show, episodes, owner.Id).ConfigureAwait(false);
            }

            done++;
            progress?.Report(done / seriesList.Count * 100);
        }

        progress?.Report(100);
    }

    private async Task ReplacePlaylistAsync(Series show, List<Episode> episodes, Guid ownerId)
    {
        var name = show.Name + PlaylistSuffix;

        foreach (var existing in _playlistManager.GetPlaylists(ownerId).Where(p => p.Name == name).ToList())
        {
            _libraryManager.DeleteItem(existing, new DeleteOptions { DeleteFileLocation = true }, true);
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var leaveOut = config.PlaylistExcludeMixed ? new[] { FillerTag, MixedTag } : new[] { FillerTag };

        var keep = episodes
            .Where(e => e.Tags == null || !e.Tags.Any(leaveOut.Contains))
            .Select(e => e.Id)
            .ToList();

        await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
        {
            Name = name,
            ItemIdList = keep,
            MediaType = MediaType.Video,
            UserId = ownerId,
            Public = true
        }).ConfigureAwait(false);

        _logger.LogInformation(
            "Kanon: built {Playlist} with {Kept} of {Total} episodes.",
            name,
            keep.Count,
            episodes.Count);
    }
}
