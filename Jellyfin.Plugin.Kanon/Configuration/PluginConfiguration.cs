using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Kanon.Configuration;

/// <summary>
/// Plugin configuration for Kanon.
/// Contains settings used by the plugin tasks and frontend injection.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets whether canon/filler badges should appear on individual episode pages.
    /// </summary>
    public bool ShowBadgesOnEpisodePage { get; set; } = true;

    /// <summary>
    /// Gets or sets whether canon/filler badges should appear in season episode lists.
    /// </summary>
    public bool ShowBadgesOnSeasonList { get; set; } = true;

    /// <summary>
    /// Gets or sets whether badges should use colored styling (e.g., red filler, green canon).
    /// </summary>
    public bool EnableBadgeColors { get; set; } = true;

    /// <summary>
    /// Gets or sets minimum delay between external DB (TheTVDB/AniDB) API requests in milliseconds to comply with rate limits and avoid temporary blocks.
    /// </summary>
    public int DbRateLimitMs { get; set; } = 2000;

    /// <summary>
    /// Determines how anime series are identified (Genre, Tag, or combination).
    /// Defaults to Genre or Tag: TMDB files anime under the "Animation" genre and marks it with an "anime" keyword tag.
    /// </summary>
    public AnimeIdentificationMode AnimeIdentificationMode { get; set; }
        = AnimeIdentificationMode.GenreOrTag;

    /// <summary>
    /// Tag name used when AnimeIdentificationMode includes Tag.
    /// </summary>
    public string AnimeTargetTag { get; set; } = "Anime";

    /// <summary>
    /// Gets or sets the ids of libraries whose every series is treated as anime, on top of the
    /// genre and tag identification.
    /// </summary>
    public string[] AnimeLibraryIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the media segment type that covers Filler episodes (a MediaSegmentType name, or empty for none).
    /// The per-user Media Segment Actions setting for that type decides whether the episode is skipped, offered for skipping or played.
    /// </summary>
    public string FillerSegmentType { get; set; } = "Commercial";

    /// <summary>
    /// Gets or sets the media segment type that covers Mixed Canon/Filler episodes (empty for none).
    /// </summary>
    public string MixedSegmentType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media segment type that covers Anime Canon episodes (empty for none).
    /// </summary>
    public string AnimeCanonSegmentType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether no-filler playlists also leave out Mixed Canon/Filler episodes.
    /// </summary>
    public bool PlaylistExcludeMixed { get; set; }
}

/// <summary>
/// Modes for identifying anime series in the library.
/// </summary>
public enum AnimeIdentificationMode
{
    /// <summary>
    /// Identify anime series by Genre only.
    /// </summary>
    Genre,
    /// <summary>
    /// Identify anime series by Tag only.
    /// </summary>
    Tag,
    /// <summary>
    /// Identify anime series by Genre or Tag.
    /// </summary>
    GenreOrTag,
    /// <summary>
    /// Identify anime series by both Genre and Tag.
    /// </summary>
    GenreAndTag
}