using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;

using Jellyfin.Plugin.Kanon.Configuration;

namespace Jellyfin.Plugin.Kanon.Segments;

/// <summary>
/// Turns an episode's canon/filler tag into a media segment spanning the whole episode,
/// so a client whose action for that segment type is Skip jumps straight to the next episode.
/// </summary>
public static class FillerSegments
{
    /// <summary>
    /// The segment provider name; Jellyfin derives the provider id stored with each segment from it.
    /// </summary>
    public const string ProviderName = "Kanon Filler Skip";

    /// <summary>
    /// Gets the provider id Jellyfin stores with segments from <see cref="ProviderName"/>
    /// (MD5 of the UTF-16 lower-cased name read as a Guid, as Jellyfin's media segment manager computes it).
    /// </summary>
    public static string ProviderId { get; } = new Guid(
        MD5.HashData(Encoding.Unicode.GetBytes(ProviderName.ToLowerInvariant()))).ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns the whole-episode segment the configuration asks for, or null when the episode is not marked.
    /// </summary>
    /// <param name="episode">The episode.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="existing">The episode's current segments; Kanon's own are ignored.</param>
    /// <returns>The segment, or null.</returns>
    public static MediaSegmentDto? For(Episode episode, PluginConfiguration config, IEnumerable<MediaSegmentDto> existing)
    {
        if (episode.RunTimeTicks is not > 0 || episode.Tags == null)
        {
            return null;
        }

        string? choice = null;
        if (episode.Tags.Contains("Filler"))
        {
            choice = config.FillerSegmentType;
        }
        else if (episode.Tags.Contains("Mixed Canon/Filler"))
        {
            choice = config.MixedSegmentType;
        }
        else if (episode.Tags.Contains("Anime Canon"))
        {
            choice = config.AnimeCanonSegmentType;
        }

        if (string.IsNullOrEmpty(choice) || !Enum.TryParse<MediaSegmentType>(choice, out var type))
        {
            return null;
        }

        // jellyfin-web acts on one segment at a time, in order, and never skips a segment whose start
        // it has already played past. When another segment (an intro) also starts at 0:00 the player
        // reaches ours only after that one ends, too late to skip it, so ours starts where that one ends.
        long runtime = episode.RunTimeTicks.Value;
        long start = existing
            .Where(s => s.StartTicks == 0 && !IsOurs(s, runtime))
            .Select(s => s.EndTicks)
            .DefaultIfEmpty(0)
            .Max();

        if (start >= runtime)
        {
            return null;
        }

        return new MediaSegmentDto
        {
            ItemId = episode.Id,
            Type = type,
            StartTicks = start,
            EndTicks = runtime
        };
    }

    /// <summary>
    /// Whether a segment is one of Kanon's: it runs to the episode's last tick and starts in the first half.
    /// Other providers' segments that run to the end (credits, previews) start well into the second half.
    /// </summary>
    private static bool IsOurs(MediaSegmentDto segment, long runtime)
        => segment.EndTicks == runtime && segment.StartTicks < runtime / 2;

    /// <summary>
    /// Replaces the episode's whole-episode skip segment with the one the current tags and configuration ask for.
    /// Segments from other providers (intros, credits) are left alone.
    /// </summary>
    /// <param name="episode">The episode.</param>
    /// <param name="segmentManager">Jellyfin's media segment manager.</param>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>Whether the episode now carries a Kanon segment.</returns>
    public static async Task<bool> SyncAsync(Episode episode, IMediaSegmentManager segmentManager, ILibraryManager libraryManager, PluginConfiguration config)
    {
        if (episode.RunTimeTicks is not > 0)
        {
            return false;
        }

        var existing = (await segmentManager.GetSegmentsAsync(episode, null, libraryManager.GetLibraryOptions(episode), false).ConfigureAwait(false)).ToList();
        var wanted = For(episode, config, existing);
        var ours = existing.Where(s => IsOurs(s, episode.RunTimeTicks.Value)).ToList();

        if (wanted != null && ours.Count == 1 && ours[0].Type == wanted.Type && ours[0].StartTicks == wanted.StartTicks)
        {
            return true;
        }

        foreach (var segment in ours)
        {
            await segmentManager.DeleteSegmentAsync(segment.Id).ConfigureAwait(false);
        }

        if (wanted != null)
        {
            await segmentManager.CreateSegmentAsync(wanted, ProviderId).ConfigureAwait(false);
        }

        return wanted != null;
    }
}

/// <summary>
/// Media segment provider that hands Jellyfin the whole-episode filler segments during media segment scans.
/// </summary>
public class FillerSegmentProvider : IMediaSegmentProvider
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="FillerSegmentProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    public FillerSegmentProvider(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public string Name => FillerSegments.ProviderName;

    /// <inheritdoc />
    public ValueTask<bool> Supports(BaseItem item) => ValueTask.FromResult(item is Episode);

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(MediaSegmentGenerationRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<MediaSegmentDto> none = Array.Empty<MediaSegmentDto>();
        if (_libraryManager.GetItemById(request.ItemId) is not Episode episode)
        {
            return Task.FromResult(none);
        }

        var segment = FillerSegments.For(
            episode,
            Plugin.Instance?.Configuration ?? new PluginConfiguration(),
            request.ExistingSegments ?? Enumerable.Empty<MediaSegmentDto>());
        return Task.FromResult(segment == null ? none : new[] { segment });
    }

    /// <inheritdoc />
    public Task CleanupExtractedData(Guid itemId, CancellationToken cancellationToken) => Task.CompletedTask;
}
