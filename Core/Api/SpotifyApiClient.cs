using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using FluentSpotify.Core.Auth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SpotifyAPI.Web;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.Core.Api;

public sealed record PlaylistDetails(
    string Id,
    string Name,
    string? Description,
    string Uri,
    string? ImageUrl,
    string OwnerName,
    int TotalItems,
    bool ContentsAvailable);

/// <summary>
/// 401 или 403 «Insufficient client scope»: у токена нет нужных разрешений —
/// пользователю нужно войти заново, чтобы выдать новые скоупы.
/// </summary>
public sealed class SpotifyScopeException : HttpRequestException
{
    public SpotifyScopeException(string message, HttpStatusCode statusCode) : base(message, null, statusCode)
    {
    }
}

public class SpotifyApiClient : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);
    // Обновляем токен заранее, чтобы запрос не ушёл с истекающим токеном
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly PkceAuthService _authService;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private volatile SpotifyClient? _client;
    private AuthTokens? _currentTokens;
    private CancellationTokenSource? _pollCts;

    public bool IsAuthenticated => _client != null;
    public bool IsConfigured => _authService.IsConfigured;

    /// <summary>
    /// Срабатывает при обновлении текущего трека или состояния воспроизведения.
    /// Вызывается из фонового потока!
    /// </summary>
    public event Action<CurrentlyPlayingContext?>? PlaybackUpdated;

    /// <summary>Сессия больше недействительна (refresh-токен отозван) — нужен повторный вход.</summary>
    public event Action? SessionExpired;

    public SpotifyApiClient(PkceAuthService authService)
    {
        _authService = authService;
    }

    public async Task InitializeAsync()
    {
        _currentTokens = await TokenStorage.LoadTokensAsync();
        if (_currentTokens == null)
        {
            return;
        }

        await EnsureValidTokenAsync();
        if (_client != null)
        {
            StartPolling();
        }
    }

    public async Task AuthenticateAsync()
    {
        var tokens = await _authService.AuthenticateAsync();
        await _tokenLock.WaitAsync();
        try
        {
            _currentTokens = tokens;
            BuildClient(tokens.AccessToken);
        }
        finally
        {
            _tokenLock.Release();
        }
        StartPolling();
    }

    public void Logout()
    {
        StopPolling();
        TokenStorage.ClearTokens();
        _currentTokens = null;
        _client = null;
        _currentUserId = null;
        PlaybackUpdated?.Invoke(null);
    }

    #region Playback Controls

    public async Task<CurrentlyPlayingContext?> GetCurrentPlaybackAsync()
    {
        var client = await GetClientAsync();
        if (client == null) return null;

        try
        {
            return await client.Player.GetCurrentPlayback();
        }
        catch (APIException ex)
        {
            AppLog.Write("SpotifyApi", $"GetCurrentPlayback: {ex.Message}");
            return null;
        }
    }

    public Task PlayAsync(string? contextUri = null, string? trackUri = null)
    {
        return ExecutePlayerAsync(client =>
        {
            var request = new PlayerResumePlaybackRequest();

            if (!string.IsNullOrEmpty(contextUri))
            {
                request.ContextUri = contextUri;
                if (!string.IsNullOrEmpty(trackUri))
                {
                    // Начать воспроизведение контекста (плейлиста/альбома) с указанного трека
                    request.OffsetParam = new PlayerResumePlaybackRequest.Offset { Uri = trackUri };
                }
            }
            else if (!string.IsNullOrEmpty(trackUri))
            {
                request.Uris = new List<string> { trackUri };
            }

            return client.Player.ResumePlayback(request);
        });
    }

    public Task PauseAsync() => ExecutePlayerAsync(c => c.Player.PausePlayback());

    public Task NextTrackAsync() => ExecutePlayerAsync(c => c.Player.SkipNext());

    public Task PreviousTrackAsync() => ExecutePlayerAsync(c => c.Player.SkipPrevious());

    public Task SeekAsync(long positionMs) =>
        ExecutePlayerAsync(c => c.Player.SeekTo(new PlayerSeekToRequest(Math.Max(0, positionMs))));

    public Task SetVolumeAsync(int volumePercent) =>
        ExecutePlayerAsync(c => c.Player.SetVolume(new PlayerVolumeRequest(Math.Clamp(volumePercent, 0, 100))));

    public Task ToggleShuffleAsync(bool state) =>
        ExecutePlayerAsync(c => c.Player.SetShuffle(new PlayerShuffleRequest(state)));

    public Task SetRepeatModeAsync(PlayerSetRepeatRequest.State mode) =>
        ExecutePlayerAsync(c => c.Player.SetRepeat(new PlayerSetRepeatRequest(mode)));

    /// <summary>
    /// Вызывается, когда команда плеера упала с «No active device»: должен найти/запустить
    /// устройство и перенести на него воспроизведение. true — можно повторить команду.
    /// </summary>
    public Func<Task<bool>>? NoActiveDeviceRecovery { get; set; }

    private async Task ExecutePlayerAsync(Func<SpotifyClient, Task<bool>> action)
    {
        var client = await GetClientAsync();
        if (client == null) return;

        try
        {
            await action(client);
        }
        catch (APIException ex) when (IsNoActiveDevice(ex) && NoActiveDeviceRecovery is { } recover)
        {
            AppLog.Write("SpotifyApi", "No active device — trying to bring up the background client");
            bool recovered;
            try
            {
                recovered = await recover();
            }
            catch (Exception recoverEx)
            {
                AppLog.Write("SpotifyApi", recoverEx);
                return;
            }
            if (!recovered) return;

            try
            {
                await action(_client ?? client);
            }
            catch (APIException retryEx)
            {
                AppLog.Write("SpotifyApi", $"Player command failed after recovery: {retryEx.Message}");
            }
        }
        catch (APIException ex)
        {
            // Например: нет Premium или устройство не поддерживает команду
            AppLog.Write("SpotifyApi", $"Player command failed: {ex.Message}");
        }
    }

    private static bool IsNoActiveDevice(APIException ex) =>
        ex.Message.Contains("No active device", StringComparison.OrdinalIgnoreCase) ||
        ex.Response?.StatusCode == HttpStatusCode.NotFound;

    #endregion

    #region Devices (Spotify Connect)

    public async Task<IReadOnlyList<Device>> GetDevicesAsync()
    {
        var client = await GetClientAsync();
        if (client == null) return Array.Empty<Device>();

        var response = await client.Player.GetAvailableDevices();
        return response.Devices ?? new List<Device>();
    }

    /// <summary>Перенести воспроизведение на устройство. play=false — не запускать музыку самим.</summary>
    public async Task<bool> TransferPlaybackAsync(string deviceId, bool play)
    {
        var client = await GetClientAsync();
        if (client == null) return false;

        try
        {
            return await client.Player.TransferPlayback(
                new PlayerTransferPlaybackRequest(new List<string> { deviceId }) { Play = play });
        }
        catch (APIException ex)
        {
            AppLog.Write("SpotifyApi", $"Transfer playback failed: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Library & Search

    public async Task<IList<FullPlaylist>> GetUserPlaylistsAsync()
    {
        var client = await GetClientAsync();
        if (client == null) return Array.Empty<FullPlaylist>();

        var firstPage = await client.Playlists.CurrentUsers(new PlaylistCurrentUsersRequest { Limit = 50 });
        return await client.PaginateAll(firstPage);
    }

    public async Task<FullPlaylist?> GetPlaylistAsync(string playlistId)
    {
        var client = await GetClientAsync();
        if (client == null) return null;

        return await client.Playlists.Get(playlistId);
    }

    public async Task<SearchResponse?> SearchAsync(string query, int limit = MaxSearchLimit)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var client = await GetClientAsync();
        if (client == null) return null;

        return await client.Search.Item(new SearchRequest(
            SearchRequest.Types.Track | SearchRequest.Types.Artist | SearchRequest.Types.Album,
            query)
        {
            // С февраля 2026 для приложений в Development Mode максимум 10 результатов на запрос
            Limit = Math.Clamp(limit, 1, MaxSearchLimit)
        });
    }

    #endregion

    #region Playlist items & Liked songs (прямые запросы)

    // В феврале 2026 Spotify изменил Web API для приложений в Development Mode:
    //  - GET /playlists/{id}/tracks → GET /playlists/{id}/items;
    //  - в объекте плейлиста поле "tracks" → "items", у элемента "track" → "item".
    // SpotifyAPI.Web 7.2.1 об этом не знает, поэтому эти данные читаем напрямую.
    // Старые имена полей тоже поддерживаем — на случай расширенного доступа (Extended Quota).

    public const int MaxSearchLimit = 10;
    public const string LikedSongsId = "__liked_songs__";

    private const string ApiBase = "https://api.spotify.com/v1/";
    private static readonly HttpClient RawHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializer SpotifyJson = JsonSerializer.Create(new JsonSerializerSettings
    {
        ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore
    });

    private string? _currentUserId;

    public async Task<PlaylistDetails?> GetPlaylistDetailsAsync(string playlistId)
    {
        var json = await GetRawAsync($"playlists/{Uri.EscapeDataString(playlistId)}");
        if (json == null) return null;

        // Содержимое отдаётся только для своих плейлистов и тех, где пользователь соавтор
        var contents = (json["items"] ?? json["tracks"]) as JObject;

        return new PlaylistDetails(
            Id: (string?)json["id"] ?? playlistId,
            Name: (string?)json["name"] ?? string.Empty,
            Description: WebUtility.HtmlDecode((string?)json["description"] ?? string.Empty),
            Uri: (string?)json["uri"] ?? string.Empty,
            ImageUrl: (string?)json["images"]?.FirstOrDefault()?["url"],
            OwnerName: (string?)json["owner"]?["display_name"] ?? "Spotify",
            TotalItems: (int?)contents?["total"] ?? 0,
            ContentsAvailable: contents != null);
    }

    /// <summary>Треки плейлиста постранично (по 50).</summary>
    public IAsyncEnumerable<IReadOnlyList<FullTrack>> EnumeratePlaylistTracksAsync(string playlistId, CancellationToken ct) =>
        EnumerateTrackPagesAsync($"playlists/{Uri.EscapeDataString(playlistId)}/items?limit=50&additional_types=track", ct);

    /// <summary>Любимые треки (медиатека пользователя) постранично (по 50).</summary>
    public IAsyncEnumerable<IReadOnlyList<FullTrack>> EnumerateSavedTracksAsync(CancellationToken ct) =>
        EnumerateTrackPagesAsync("me/tracks?limit=50", ct);

    public async Task<int> GetSavedTracksTotalAsync()
    {
        var json = await GetRawAsync("me/tracks?limit=1");
        return (int?)json?["total"] ?? 0;
    }

    /// <summary>Контекст "Любимые треки" для воспроизведения: spotify:user:{id}:collection</summary>
    public async Task<string?> GetLikedSongsContextUriAsync()
    {
        string? userId = await GetCurrentUserIdAsync();
        return userId == null ? null : $"spotify:user:{userId}:collection";
    }

    /// <summary>Id текущего пользователя Spotify (запрашивается один раз за сессию).</summary>
    public async Task<string?> GetCurrentUserIdAsync()
    {
        if (_currentUserId == null)
        {
            var me = await GetRawAsync("me");
            _currentUserId = (string?)me?["id"];
        }
        return _currentUserId;
    }

    private async IAsyncEnumerable<IReadOnlyList<FullTrack>> EnumerateTrackPagesAsync(
        string firstUrl, [EnumeratorCancellation] CancellationToken ct)
    {
        string? url = firstUrl;
        while (url != null && !ct.IsCancellationRequested)
        {
            var page = await GetRawAsync(url, ct);
            if (page == null) yield break;

            var tracks = new List<FullTrack>();
            foreach (var entry in page["items"] as JArray ?? new JArray())
            {
                // Новое имя поля — "item", старое — "track"; эпизоды подкастов пропускаем
                if ((entry["item"] ?? entry["track"]) is JObject item && (string?)item["type"] == "track")
                {
                    var track = item.ToObject<FullTrack>(SpotifyJson);
                    if (track != null && !string.IsNullOrEmpty(track.Uri)) tracks.Add(track);
                }
            }

            yield return tracks;
            url = (string?)page["next"];
        }
    }

    private Task<JObject?> GetRawAsync(string url, CancellationToken ct = default) =>
        SendRawAsync(HttpMethod.Get, url, null, ct);

    private async Task<JObject?> SendRawAsync(HttpMethod method, string url, JObject? body, CancellationToken ct = default)
    {
        await EnsureValidTokenAsync();
        string? token = _currentTokens?.AccessToken;
        if (token == null) return null;

        var uri = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new Uri(url) : new Uri(ApiBase + url);

        for (int attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null)
            {
                request.Content = new StringContent(body.ToString(Formatting.None), System.Text.Encoding.UTF8, "application/json");
            }

            using var response = await RawHttp.SendAsync(request, ct);
            string responseBody = await response.Content.ReadAsStringAsync(ct);

            // Rate limit: ждём, сколько попросил сервер, и пробуем ещё раз
            if ((int)response.StatusCode == 429 && attempt < 3)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                await Task.Delay(delay, ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                string message = $"Spotify API {(int)response.StatusCode} ({uri.AbsolutePath}): {responseBody}";

                // Токен выдан без нужного скоупа (например, сохранён до появления новой функции)
                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    (response.StatusCode == HttpStatusCode.Forbidden &&
                     responseBody.Contains("scope", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new SpotifyScopeException(message, response.StatusCode);
                }

                throw new HttpRequestException(message, null, response.StatusCode);
            }

            // 201/200 без тела (или пустой снимок) — возвращаем пустой объект
            return string.IsNullOrWhiteSpace(responseBody) ? new JObject() : JObject.Parse(responseBody);
        }
    }

    #endregion

    #region Top items & playlist creation («Мои итоги»)

    // GET /me/top/{type} в Development Mode остался (скоуп user-top-read).
    // Создание плейлиста: POST /users/{id}/playlists удалён в феврале 2026 → POST /me/playlists;
    // добавление треков: /playlists/{id}/tracks → /playlists/{id}/items.

    public const int MaxTopItemsLimit = 50;
    private const int MaxItemsPerAddRequest = 100;

    /// <summary>Выдан ли текущему токену скоуп; null — неизвестно (токен сохранён старой версией).</summary>
    public bool? HasScope(string scope) => _currentTokens?.HasScope(scope);

    /// <param name="timeRange">long_term (~год), medium_term (~6 месяцев) или short_term (~4 недели).</param>
    public async Task<IReadOnlyList<FullTrack>> GetTopTracksAsync(string timeRange, int limit = MaxTopItemsLimit,
        CancellationToken ct = default)
    {
        var json = await GetRawAsync(TopUrl("tracks", timeRange, limit), ct);
        return ReadItems<FullTrack>(json, t => !string.IsNullOrEmpty(t.Uri));
    }

    /// <param name="timeRange">long_term (~год), medium_term (~6 месяцев) или short_term (~4 недели).</param>
    public async Task<IReadOnlyList<FullArtist>> GetTopArtistsAsync(string timeRange, int limit = MaxTopItemsLimit,
        CancellationToken ct = default)
    {
        var json = await GetRawAsync(TopUrl("artists", timeRange, limit), ct);
        return ReadItems<FullArtist>(json, a => !string.IsNullOrEmpty(a.Id));
    }

    /// <summary>Создать плейлист в медиатеке текущего пользователя и заполнить его треками.</summary>
    /// <returns>Id созданного плейлиста.</returns>
    public async Task<string> CreatePlaylistWithTracksAsync(string name, string? description, bool isPublic,
        IReadOnlyList<string> trackUris, CancellationToken ct = default)
    {
        var created = await SendRawAsync(HttpMethod.Post, "me/playlists", new JObject
        {
            ["name"] = name,
            ["public"] = isPublic,
            ["description"] = description ?? string.Empty
        }, ct);

        string playlistId = (string?)created?["id"]
            ?? throw new InvalidOperationException(Localizer.Get("Api_NoPlaylistId"));

        for (int i = 0; i < trackUris.Count; i += MaxItemsPerAddRequest)
        {
            var chunk = trackUris.Skip(i).Take(MaxItemsPerAddRequest);
            await SendRawAsync(HttpMethod.Post, $"playlists/{Uri.EscapeDataString(playlistId)}/items",
                new JObject { ["uris"] = new JArray(chunk) }, ct);
        }

        return playlistId;
    }

    /// <summary>Воспроизвести список треков, начиная с указанного (без контекста плейлиста).</summary>
    public Task PlayTracksAsync(IReadOnlyList<string> trackUris, string? startUri = null)
    {
        if (trackUris.Count == 0) return Task.CompletedTask;

        return ExecutePlayerAsync(client => client.Player.ResumePlayback(new PlayerResumePlaybackRequest
        {
            Uris = trackUris.ToList(),
            OffsetParam = startUri == null ? null : new PlayerResumePlaybackRequest.Offset { Uri = startUri }
        }));
    }

    private static string TopUrl(string type, string timeRange, int limit) =>
        $"me/top/{type}?time_range={Uri.EscapeDataString(timeRange)}&limit={Math.Clamp(limit, 1, MaxTopItemsLimit)}";

    private static List<T> ReadItems<T>(JObject? page, Func<T, bool> isValid) where T : class
    {
        var result = new List<T>();
        foreach (var entry in page?["items"] as JArray ?? new JArray())
        {
            if (entry is JObject obj && obj.ToObject<T>(SpotifyJson) is { } item && isValid(item))
            {
                result.Add(item);
            }
        }
        return result;
    }

    #endregion

    #region Internal Lifecycle & Polling

    private async Task<SpotifyClient?> GetClientAsync()
    {
        await EnsureValidTokenAsync();
        return _client;
    }

    private async Task EnsureValidTokenAsync()
    {
        if (_currentTokens == null) return;

        // Одновременный refresh из UI и из цикла опроса приводил бы к двойному обмену токена
        await _tokenLock.WaitAsync();
        try
        {
            var tokens = _currentTokens;
            if (tokens == null) return;

            if (DateTime.UtcNow >= tokens.ExpiresAt - RefreshMargin)
            {
                try
                {
                    tokens = await _authService.RefreshTokenAsync(tokens.RefreshToken);
                    _currentTokens = tokens;
                    BuildClient(tokens.AccessToken);
                }
                catch (SpotifyAuthException ex) when (ex.RequiresReauthentication)
                {
                    AppLog.Write("SpotifyApi", ex);
                    StopPolling();
                    TokenStorage.ClearTokens();
                    _currentTokens = null;
                    _client = null;
                    SessionExpired?.Invoke();
                }
            }
            else if (_client == null)
            {
                BuildClient(tokens.AccessToken);
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void BuildClient(string token)
    {
        var config = SpotifyClientConfig.CreateDefault().WithToken(token);
        _client = new SpotifyClient(config);
    }

    private void StartPolling()
    {
        StopPolling();
        var cts = new CancellationTokenSource();
        _pollCts = cts;
        _ = Task.Run(() => PollPlaybackLoopAsync(cts.Token));
    }

    private void StopPolling()
    {
        var cts = Interlocked.Exchange(ref _pollCts, null);
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task PollPlaybackLoopAsync(CancellationToken ct)
    {
        // У каждого цикла свой таймер: PeriodicTimer не допускает конкурентных ожиданий
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                try
                {
                    var playback = await GetCurrentPlaybackAsync();
                    if (!ct.IsCancellationRequested)
                    {
                        PlaybackUpdated?.Invoke(playback);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Единичные сетевые сбои между тиками таймера не должны останавливать опрос
                    AppLog.Write("SpotifyApi", $"Polling failed: {ex.Message}");
                }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // Опрос остановлен
        }
    }

    public void Dispose()
    {
        StopPolling();
        _tokenLock.Dispose();
    }

    #endregion
}
