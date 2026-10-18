using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentSpotify.Core.Updates;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.Core.NowPlaying;

/// <summary>Строка текста песни; Time — начало строки (null, если текст без синхронизации).</summary>
public sealed record LyricsLine(TimeSpan? Time, string Text);

/// <summary>Текст песни: синхронизированный (LRC) или обычный; у инструментальных треков строк нет.</summary>
public sealed record Lyrics(IReadOnlyList<LyricsLine> Lines, bool IsSynced, bool IsInstrumental);

/// <summary>
/// Тексты песен из LRCLIB (https://lrclib.net) — открытого стороннего сервиса: в публичном
/// Web API Spotify текстов нет. Ищем по артисту, названию, альбому и длительности,
/// результаты (в том числе «не найдено») кешируем на время работы приложения.
/// </summary>
public sealed partial class LyricsService
{
    private const string ApiBase = "https://lrclib.net/api/";
    // LRCLIB сопоставляет длительность с точностью ±2 с; при поиске берём чуть шире
    private const double DurationToleranceSeconds = 3;
    private const int MaxCacheEntries = 200;

    private readonly HttpClient _http;
    private readonly Dictionary<string, Lyrics?> _cache = new();
    private readonly LinkedList<string> _cacheOrder = new();

    public LyricsService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // LRCLIB просит указывать в User-Agent приложение и ссылку на него
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FluentSpotify", AppVersion.Current.Text));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/justaprunk/fluent-spotify)"));
    }

    /// <param name="cacheKey">Ключ кеша — обычно id трека Spotify.</param>
    /// <returns>Текст или null, если LRCLIB его не знает или сервис недоступен.</returns>
    public async Task<Lyrics?> GetAsync(string cacheKey, string artist, string track, string? album, TimeSpan duration,
        CancellationToken ct)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(cacheKey, out var cached)) return cached;
        }

        Lyrics? result;
        try
        {
            result = await FetchExactAsync(artist, track, album, duration, ct)
                ?? await SearchAsync(artist, track, duration, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Сеть или сам сервис: не кешируем, при следующем показе трека попробуем снова
            AppLog.Write("Lyrics", $"LRCLIB: {ex.Message}");
            return null;
        }

        lock (_cache)
        {
            if (_cache.TryAdd(cacheKey, result))
            {
                _cacheOrder.AddLast(cacheKey);
                if (_cacheOrder.Count > MaxCacheEntries)
                {
                    _cache.Remove(_cacheOrder.First!.Value);
                    _cacheOrder.RemoveFirst();
                }
            }
        }
        return result;
    }

    private async Task<Lyrics?> FetchExactAsync(string artist, string track, string? album, TimeSpan duration,
        CancellationToken ct)
    {
        string url = $"{ApiBase}get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(track)}"
            + (string.IsNullOrEmpty(album) ? string.Empty : $"&album_name={Uri.EscapeDataString(album)}")
            + $"&duration={(int)Math.Round(duration.TotalSeconds)}";

        using var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return FromRecord(doc.RootElement);
    }

    /// <summary>Запасной поиск: альбом в LRCLIB часто записан иначе (сингл, переиздание).</summary>
    private async Task<Lyrics?> SearchAsync(string artist, string track, TimeSpan duration, CancellationToken ct)
    {
        string url = $"{ApiBase}search?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(track)}";
        using var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        // Только записи той же длительности: иначе легко попасть на live- или remix-версию.
        // Среди подходящих предпочитаем синхронизированный текст.
        JsonElement? best = null;
        foreach (var record in doc.RootElement.EnumerateArray())
        {
            if (!record.TryGetProperty("duration", out var d) || d.ValueKind != JsonValueKind.Number) continue;
            if (Math.Abs(d.GetDouble() - duration.TotalSeconds) > DurationToleranceSeconds) continue;

            if (best == null || (!HasText(best.Value, "syncedLyrics") && HasText(record, "syncedLyrics")))
            {
                best = record;
            }
        }
        return best is { } found ? FromRecord(found) : null;
    }

    private static Lyrics? FromRecord(JsonElement record)
    {
        if (record.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True)
        {
            return new Lyrics([], IsSynced: false, IsInstrumental: true);
        }

        if (GetText(record, "syncedLyrics") is { } synced && ParseLrc(synced) is { Count: > 0 } lines)
        {
            return new Lyrics(lines, IsSynced: true, IsInstrumental: false);
        }

        if (GetText(record, "plainLyrics") is { } plain)
        {
            var plainLines = plain.Replace("\r", string.Empty).Split('\n')
                .Select(l => new LyricsLine(null, l.Trim()))
                .ToList();
            TrimEmptyLines(plainLines);
            if (plainLines.Count > 0) return new Lyrics(plainLines, IsSynced: false, IsInstrumental: false);
        }
        return null;
    }

    /// <summary>
    /// LRC: «[мм:сс.хх]текст», у одной строки может быть несколько меток (повторяющийся припев).
    /// Пустые строки с меткой — паузы: оставляем, чтобы подсветка не «залипала» на предыдущей строке.
    /// </summary>
    public static List<LyricsLine> ParseLrc(string lrc)
    {
        var lines = new List<LyricsLine>();
        foreach (string raw in lrc.Replace("\r", string.Empty).Split('\n'))
        {
            var matches = TimestampRegex().Matches(raw);
            if (matches.Count == 0) continue;

            string text = raw[(matches[^1].Index + matches[^1].Length)..].Trim();
            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) ||
                    !double.TryParse(match.Groups[2].Value.Replace(':', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                {
                    continue;
                }
                lines.Add(new LyricsLine(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds), text));
            }
        }

        lines.Sort((a, b) => a.Time!.Value.CompareTo(b.Time!.Value));
        TrimEmptyLines(lines);
        return lines;
    }

    private static void TrimEmptyLines(List<LyricsLine> lines)
    {
        while (lines.Count > 0 && lines[^1].Text.Length == 0) lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Text.Length == 0) lines.RemoveAt(0);
    }

    private static bool HasText(JsonElement record, string property) => GetText(record, property) != null;

    private static string? GetText(JsonElement record, string property) =>
        record.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]")]
    private static partial Regex TimestampRegex();
}
