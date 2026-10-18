using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentSpotify.Core.Updates;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.Core.NowPlaying;

/// <summary>Краткое описание артиста из Википедии и ссылка на статью (для указания источника).</summary>
public sealed record ArtistBio(string Text, string PageUrl, string WikiLanguage);

/// <summary>
/// Биография артиста: в Web API Spotify её нет. Статью находим не по имени (тёзки, одноимённые
/// слова), а через Wikidata по свойству P1902 «Spotify artist ID» — это точное совпадение.
/// Если такого элемента нет, их несколько или статьи нет ни на языке интерфейса, ни на английском —
/// биографию не показываем.
/// </summary>
public sealed class ArtistBioService
{
    private const string WikidataApi = "https://www.wikidata.org/w/api.php";
    private const int MaxCacheEntries = 100;

    private readonly HttpClient _http;
    private readonly Dictionary<string, ArtistBio?> _cache = new();
    private readonly LinkedList<string> _cacheOrder = new();

    public ArtistBioService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // Правила Wikimedia: User-Agent с названием приложения и контактом
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FluentSpotify", AppVersion.Current.Text));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/justaprunk/fluent-spotify)"));
    }

    public async Task<ArtistBio?> GetAsync(string spotifyArtistId, CancellationToken ct)
    {
        string uiLanguage = Localizer.CurrentLanguage;
        string key = $"{uiLanguage}:{spotifyArtistId}";
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }

        ArtistBio? result;
        try
        {
            result = await FetchAsync(spotifyArtistId, uiLanguage, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Write("ArtistBio", $"Wikipedia: {ex.Message}");
            return null;
        }

        lock (_cache)
        {
            if (_cache.TryAdd(key, result))
            {
                _cacheOrder.AddLast(key);
                if (_cacheOrder.Count > MaxCacheEntries)
                {
                    _cache.Remove(_cacheOrder.First!.Value);
                    _cacheOrder.RemoveFirst();
                }
            }
        }
        return result;
    }

    private async Task<ArtistBio?> FetchAsync(string spotifyArtistId, string uiLanguage, CancellationToken ct)
    {
        // 1. Элемент Wikidata с этим Spotify ID — ровно один, иначе совпадение ненадёжно
        string search = $"{WikidataApi}?action=query&list=search&format=json&srlimit=2&srsearch="
            + Uri.EscapeDataString($"haswbstatement:P1902={spotifyArtistId}");
        using var searchDoc = await GetJsonAsync(search, null, ct);
        if (searchDoc == null
            || !searchDoc.RootElement.TryGetProperty("query", out var query)
            || !query.TryGetProperty("search", out var hits)
            || hits.GetArrayLength() != 1
            || hits[0].GetProperty("title").GetString() is not { Length: > 0 } entityId)
        {
            return null;
        }

        // 2. Статьи этого элемента на языке интерфейса и на английском
        string wiki = WikiLanguage(uiLanguage);
        string sites = wiki == "en" ? "enwiki" : $"{wiki}wiki|enwiki";
        string entityUrl = $"{WikidataApi}?action=wbgetentities&format=json&props=sitelinks&ids={Uri.EscapeDataString(entityId)}"
            + $"&sitefilter={Uri.EscapeDataString(sites)}";
        using var entityDoc = await GetJsonAsync(entityUrl, null, ct);
        if (entityDoc == null
            || !entityDoc.RootElement.TryGetProperty("entities", out var entities)
            || !entities.TryGetProperty(entityId, out var entity)
            || !entity.TryGetProperty("sitelinks", out var sitelinks))
        {
            return null;
        }

        string[] languages = wiki == "en" ? ["en"] : [wiki, "en"];
        foreach (string lang in languages)
        {
            if (!sitelinks.TryGetProperty($"{lang}wiki", out var link)
                || link.GetProperty("title").GetString() is not { Length: > 0 } title)
            {
                continue;
            }

            // 3. Вступление статьи (REST summary); у китайской Википедии вариант письма — через Accept-Language
            string summaryUrl = $"https://{lang}.wikipedia.org/api/rest_v1/page/summary/"
                + Uri.EscapeDataString(title.Replace(' ', '_'));
            string? variant = lang == "zh" ? (uiLanguage == "zh-Hant" ? "zh-tw" : "zh-cn") : null;
            using var summary = await GetJsonAsync(summaryUrl, variant, ct);
            if (summary == null) continue;

            var root = summary.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() != "standard") continue;
            if (!root.TryGetProperty("extract", out var extract) || extract.GetString() is not { Length: > 0 } text) continue;

            string pageUrl = root.TryGetProperty("content_urls", out var urls)
                && urls.TryGetProperty("desktop", out var desktop)
                && desktop.TryGetProperty("page", out var page)
                && page.GetString() is { Length: > 0 } p
                    ? p
                    : $"https://{lang}.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";

            return new ArtistBio(text.Trim(), pageUrl, lang);
        }
        return null;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, string? acceptLanguage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (acceptLanguage != null) request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Код раздела Википедии для языка интерфейса: pt-BR → pt, zh-Hans/zh-Hant → zh.</summary>
    private static string WikiLanguage(string uiLanguage) => uiLanguage.Split('-')[0].ToLowerInvariant();
}
