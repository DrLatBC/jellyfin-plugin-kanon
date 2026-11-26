using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using Jellyfin.Data.Enums;

using Jellyfin.Plugin.Kanon.Configuration;


namespace Jellyfin.Plugin.Kanon.Helpers;

/// <summary>
/// Collects anime series from the Jellyfin library based on the plugin configuration.
/// </summary>
public static class CollectAnimeSeries
{
    /// <summary>
    /// Executes the collection of anime series from the library.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager used to query media items.</param>
    /// <returns>An immutable list of series identified as anime.</returns>
    public static IReadOnlyList<Series> Execute(ILibraryManager libraryManager)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        var allSeries = libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [ BaseItemKind.Series ],
            Recursive = true
        })
        .Cast<Series>();

        var animeLibraries = (config.AnimeLibraryIds ?? Array.Empty<string>())
            .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
            .Where(guid => guid != Guid.Empty)
            .ToHashSet();

        return allSeries
            .Where(s => InAnimeLibrary(s, animeLibraries, libraryManager) || IsAnime(s, config))
            .ToList()
            .AsReadOnly();
    }

    // Every series in a library the user marked as anime counts, whatever its genres and tags say:
    // providers such as Shoko or AniDB rarely label anime with an "Anime" genre or tag.
    private static bool InAnimeLibrary(Series series, HashSet<Guid> animeLibraries, ILibraryManager libraryManager)
    {
        if (animeLibraries.Count == 0)
        {
            return false;
        }

        return libraryManager.GetCollectionFolders(series).Any(folder => animeLibraries.Contains(folder.Id));
    }

    private static bool IsAnime(Series series, PluginConfiguration config)
    {
        var hasGenre = series.Genres?.Any(
            g => string.Equals(g, "Anime", StringComparison.OrdinalIgnoreCase)) == true;

        var targetTag = string.IsNullOrWhiteSpace(config.AnimeTargetTag) ? "Anime" : config.AnimeTargetTag;

        var hasTag = series.Tags?.Any(
            t => string.Equals(t, targetTag ?? "Anime", StringComparison.OrdinalIgnoreCase)) == true;


        return config.AnimeIdentificationMode switch
        {
            AnimeIdentificationMode.Genre => hasGenre,
            AnimeIdentificationMode.Tag => hasTag,
            AnimeIdentificationMode.GenreOrTag => hasGenre || hasTag,
            AnimeIdentificationMode.GenreAndTag => hasGenre && hasTag,
            _ => hasGenre
        };
    }
}