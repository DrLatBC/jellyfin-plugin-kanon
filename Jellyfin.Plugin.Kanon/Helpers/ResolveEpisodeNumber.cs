using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MediaBrowser.Controller.Entities.TV;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Jellyfin.Plugin.Kanon.Helpers;

/// <summary>
/// Provides utilities to resolve absolute episode numbers
/// from AniDB and TheTVDB.
/// </summary>
public static class ResolveEpisodeNumber
{
    /// <summary>
    /// Resolves absolute episode numbers from the episodes' own season and episode numbers,
    /// for series matched by providers that carry no absolute numbering (e.g. TMDB).
    /// Specials (season 0) are not numbered.
    /// </summary>
    /// <param name="episodes">The series' episodes.</param>
    /// <returns>Absolute episode number keyed by episode id.</returns>
    public static IReadOnlyDictionary<Guid, int> AbsoluteFromLibrary(IEnumerable<Episode> episodes)
    {
        var seasons = episodes
            .Where(e => e.IndexNumber.HasValue && (e.ParentIndexNumber ?? 1) > 0)
            .GroupBy(e => e.ParentIndexNumber ?? 1)
            .OrderBy(g => g.Key)
            .ToList();

        // Providers such as TMDB number long anime continuously across seasons
        // (Naruto season 2 starts at episode 53); then the episode number already is the absolute number.
        // Otherwise numbering restarts each season and earlier seasons' episodes are counted in.
        bool continuous = seasons.Skip(1).All(g => g.Min(e => e.IndexNumber!.Value) > 1);

        var result = new Dictionary<Guid, int>();
        int offset = 0;
        foreach (var season in seasons)
        {
            foreach (var episode in season)
            {
                result[episode.Id] = continuous ? episode.IndexNumber!.Value : offset + episode.IndexNumber!.Value;
            }

            offset += season.Max(e => e.IndexNumberEnd ?? e.IndexNumber!.Value);
        }

        return result;
    }

    /// <summary>
    /// Resolves an absolute episode number from AniDB given an episode ID.
    /// </summary>
    /// <returns>The absolute episode number or null if not found.</returns>
    public static async Task<int?> AbsoluteFromAniDbAsync(string? anidbEpisodeId, HttpClient client, int RequestDelayMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(anidbEpisodeId)) return null;
        var url = $"https://anidb.net/episode/{anidbEpisodeId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko)");

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var match = Regex.Match(html, @"- (\d+) -");

        await Task.Delay(RequestDelayMs, cancellationToken).ConfigureAwait(false);

        if (!match.Success) return null;

        return int.TryParse(
            match.Groups[1].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var absolute)
            ? absolute
            : null;
    }

    /// <summary>
    /// Resolves an absolute episode number from TheTVDB.
    /// Requires the Tvdb series ID or slug and the episode ID.
    /// </summary>
    /// <returns>The absolute episode number or null if not resolvable.</returns>
    public static async Task<int?> AbsoluteFromTvdbAsync(string? tvdbSeriesId, string? tvdbSeriesSlug, string? tvdbEpisodeId, HttpClient client, int RequestDelayMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tvdbEpisodeId)) return null;
        string? url = tvdbSeriesId is not null
            ? $"https://www.thetvdb.com/series/{tvdbSeriesId}/episodes/{tvdbEpisodeId}"
            : tvdbSeriesSlug is not null
                ? $"https://www.thetvdb.com/series/{tvdbSeriesSlug}/episodes/{tvdbEpisodeId}"
                : null;

        if (url is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        await Task.Delay(RequestDelayMs, cancellationToken).ConfigureAwait(false);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var absoluteCrumb = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'crumbs')][.//a[contains(@href,'/seasons/absolute/')]]");

        if (absoluteCrumb is null) return null;

        var match = Regex.Match(absoluteCrumb.InnerText, @"Episode\s+(\d+)", RegexOptions.IgnoreCase);

        if (!match.Success) return null;

        return int.TryParse(
                match.Groups[1].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var absolute)
            ? absolute
            : null;
    }
}