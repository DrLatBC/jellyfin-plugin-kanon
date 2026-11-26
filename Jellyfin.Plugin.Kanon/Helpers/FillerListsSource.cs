using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Kanon.Helpers;

/// <summary>
/// Reads filler data from FillerLists (https://fillerlists.com/data, CC BY-SA 4.0), which covers a set of
/// long-running anime and identifies each by its TMDB, TheTVDB and IMDb ids, so series match by id
/// instead of by a guessed page name.
/// </summary>
public sealed class FillerListsSource
{
    private const string IndexUrl = "https://fillerlists.com/data/shows.json";

    // FillerLists episode types mapped onto the tags Kanon writes (Anime Filler List's names).
    // "unknown" (FillerLists' two sources disagree) is left untagged.
    private static readonly Dictionary<string, string> TypeTags = new(StringComparer.Ordinal)
    {
        ["canon"] = "Manga Canon",
        ["mixed"] = "Mixed Canon/Filler",
        ["filler"] = "Filler",
        ["anime-canon"] = "Anime Canon"
    };

    private readonly List<(string Provider, string Id, string JsonUrl)> _shows;

    private FillerListsSource(List<(string Provider, string Id, string JsonUrl)> shows)
    {
        _shows = shows;
    }

    /// <summary>
    /// Downloads the FillerLists show index.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The source, ready to match series.</returns>
    public static async Task<FillerListsSource> LoadAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(httpClient, IndexUrl, cancellationToken).ConfigureAwait(false);

        var shows = new List<(string Provider, string Id, string JsonUrl)>();
        foreach (var show in doc.RootElement.GetProperty("shows").EnumerateArray())
        {
            if (!show.TryGetProperty("json", out var json) || !show.TryGetProperty("ids", out var ids))
            {
                continue;
            }

            foreach (var (key, provider) in new[] { ("tmdb", "Tmdb"), ("tvdb", "Tvdb"), ("imdb", "Imdb") })
            {
                if (ids.TryGetProperty(key, out var id) && id.ValueKind != JsonValueKind.Null)
                {
                    shows.Add((provider, id.ToString(), json.GetString() ?? string.Empty));
                }
            }
        }

        return new FillerListsSource(shows);
    }

    /// <summary>
    /// Finds the series on FillerLists by its provider ids.
    /// </summary>
    /// <param name="series">The series.</param>
    /// <returns>The series' FillerLists data URL, or null when FillerLists does not cover it.</returns>
    public string? Match(Series series)
    {
        foreach (var (provider, id, jsonUrl) in _shows)
        {
            if (string.Equals(series.GetProviderId(provider), id, StringComparison.OrdinalIgnoreCase) && jsonUrl.Length > 0)
            {
                return jsonUrl;
            }
        }

        return null;
    }

    /// <summary>
    /// Downloads one show's episode types.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="jsonUrl">The show's data URL from <see cref="Match"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tag name keyed by absolute episode number.</returns>
    public static async Task<Dictionary<int, string>> LoadEpisodesAsync(HttpClient httpClient, string jsonUrl, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(httpClient, jsonUrl, cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<int, string>();
        foreach (var episode in doc.RootElement.GetProperty("episodes").EnumerateArray())
        {
            if (episode.TryGetProperty("n", out var n) && n.TryGetInt32(out var number)
                && episode.TryGetProperty("type", out var type)
                && TypeTags.TryGetValue(type.GetString() ?? string.Empty, out var tag))
            {
                result[number] = tag;
            }
        }

        return result;
    }

    // Identifies Kanon to FillerLists, so its maintainer can see where requests come from.
    private static async Task<JsonDocument> GetJsonAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd($"Kanon/{typeof(FillerListsSource).Assembly.GetName().Version} (+https://github.com/DrLatBC/jellyfin-plugin-kanon)");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
