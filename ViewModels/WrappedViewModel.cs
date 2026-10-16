using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentSpotify.Core.Api;
using FluentSpotify.Shared.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using SpotifyAPI.Web;

namespace FluentSpotify.ViewModels;

/// <summary>Период статистики Spotify (параметр time_range).</summary>
public sealed record WrappedPeriod(string TimeRange, string Label);

public sealed class RankedTrackItem
{
    public int Rank { get; init; }
    public string RankText => Rank.ToString(CultureInfo.InvariantCulture);
    public TrackDisplayItem Track { get; init; } = new();
    public string Title => Track.Title;
    public string Artist => Track.Artist;
    public string Album => Track.Album;
    public string Duration => Track.Duration;
    public string? ImageUrl => Track.ImageUrl;
}

public sealed class RankedArtistItem
{
    public int Rank { get; init; }
    public string RankText => Rank.ToString(CultureInfo.InvariantCulture);
    public string Name { get; init; } = string.Empty;
    public string Uri { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public string Genres { get; init; } = string.Empty;
    public bool HasGenres => Genres.Length > 0;
}

public sealed class GenreItem
{
    public int Rank { get; init; }
    public string RankText => Rank.ToString(CultureInfo.InvariantCulture);
    public string Name { get; init; } = string.Empty;
    /// <summary>Вес жанра относительно самого популярного, 0–100 (для полосы).</summary>
    public double Share { get; init; }
    public string ArtistsText { get; init; } = string.Empty;
}

/// <summary>
/// «Мои итоги»: топ треков, артистов и жанров за период — аналог Spotify Wrapped
/// на основе GET /me/top/{tracks|artists}.
/// </summary>
public partial class WrappedViewModel : ObservableObject
{
    // В сетке артистов — топ-20, чтобы список треков не уезжал далеко вниз; жанры считаем по всем 50
    private const int ArtistsShown = 20;

    private const string ReloginMessage =
        "Для «Моих итогов» приложению нужны новые разрешения Spotify: статистика прослушиваний " +
        "и создание плейлистов. Войдите в аккаунт ещё раз и подтвердите доступ — медиатека и настройки сохранятся.";

    private sealed record PeriodData(IReadOnlyList<FullTrack> Tracks, IReadOnlyList<FullArtist> Artists);

    private readonly SpotifyApiClient _apiClient;
    private readonly MainViewModel _main;
    // Итоги за период меняются раз в сутки — повторно при переключении вкладок не грузим
    private readonly Dictionary<string, PeriodData> _cache = new();
    private CancellationTokenSource? _loadCts;

    public IReadOnlyList<WrappedPeriod> Periods { get; } =
    [
        new("long_term", "Примерно год"),
        new("medium_term", "6 месяцев"),
        new("short_term", "4 недели")
    ];

    [ObservableProperty]
    private WrappedPeriod _selectedPeriod;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePlaylistCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData), nameof(HasGenres), nameof(HasNoGenres))]
    [NotifyCanExecuteChangedFor(nameof(SavePlaylistCommand))]
    private bool _isLoaded;

    // Сообщение о проблеме / успехе в InfoBar
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusOpen))]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _statusTitle;

    [ObservableProperty]
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private bool _needsRelogin;

    public bool IsStatusOpen => !string.IsNullOrEmpty(StatusMessage);

    // Карточки-итоги: №1 за период
    [ObservableProperty] private string? _topTrackTitle;
    [ObservableProperty] private string? _topTrackArtist;
    [ObservableProperty] private string? _topTrackImageUrl;
    [ObservableProperty] private string? _topArtistName;
    [ObservableProperty] private string? _topArtistImageUrl;
    [ObservableProperty] private string? _topGenreName;
    [ObservableProperty] private string? _topGenreDetails;

    public ObservableCollection<RankedTrackItem> TopTracks { get; } = new();
    public ObservableCollection<RankedArtistItem> TopArtists { get; } = new();
    public ObservableCollection<GenreItem> TopGenres { get; } = new();

    public bool HasData => IsLoaded && (TopTracks.Count > 0 || TopArtists.Count > 0);
    public bool HasGenres => IsLoaded && TopGenres.Count > 0;
    public bool HasNoGenres => IsLoaded && TopArtists.Count > 0 && TopGenres.Count == 0;

    public WrappedViewModel(SpotifyApiClient apiClient, MainViewModel main)
    {
        _apiClient = apiClient;
        _main = main;
        _selectedPeriod = Periods[0];
    }

    partial void OnSelectedPeriodChanged(WrappedPeriod value) => _ = LoadAsync();

    public async Task LoadAsync(bool force = false)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var ct = cts.Token;
        var period = SelectedPeriod;

        IsLoading = true;
        IsLoaded = false;
        ClearStatus();

        try
        {
            if (_apiClient.HasScope(Scopes.UserTopRead) == false)
            {
                Clear();
                ShowRelogin();
                return;
            }

            if (force || !_cache.TryGetValue(period.TimeRange, out var data))
            {
                var tracksTask = _apiClient.GetTopTracksAsync(period.TimeRange, ct: ct);
                var artistsTask = _apiClient.GetTopArtistsAsync(period.TimeRange, ct: ct);
                data = new PeriodData(await tracksTask, await artistsTask);
                _cache[period.TimeRange] = data;
            }

            if (ct.IsCancellationRequested) return;
            Apply(data);

            if (TopTracks.Count == 0 && TopArtists.Count == 0)
            {
                ShowStatus(InfoBarSeverity.Informational, "Пока нечего подводить",
                    $"Spotify ещё не собрал статистику за период «{period.Label}». " +
                    "Послушайте музыку побольше или выберите более длинный период.");
            }
        }
        catch (OperationCanceledException)
        {
            // Переключили период или ушли со страницы
        }
        catch (SpotifyScopeException ex)
        {
            AppLog.Write("WrappedViewModel", ex);
            if (ct.IsCancellationRequested) return;
            Clear();
            ShowRelogin();
        }
        catch (Exception ex)
        {
            AppLog.Write("WrappedViewModel", ex);
            if (ct.IsCancellationRequested) return;
            Clear();
            ShowStatus(InfoBarSeverity.Error, "Не удалось загрузить итоги", DescribeError(ex));
        }
        finally
        {
            if (_loadCts == cts)
            {
                IsLoading = false;
                IsLoaded = !ct.IsCancellationRequested;
            }
        }
    }

    /// <summary>Остановить загрузку (при уходе со страницы).</summary>
    public void CancelLoading() => _loadCts?.Cancel();

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(force: true);

    [RelayCommand]
    private async Task ReloginAsync()
    {
        IsLoading = true;
        ClearStatus();
        try
        {
            await _main.ReauthenticateAsync();
            _cache.Clear();
        }
        catch (Exception ex)
        {
            AppLog.Write("WrappedViewModel", ex);
            IsLoading = false;
            ShowRelogin();
            StatusMessage = $"Вход не завершён: {ex.Message}";
            return;
        }

        await LoadAsync(force: true);
    }

    [RelayCommand]
    private async Task PlayTrackAsync(RankedTrackItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Track.Uri)) return;

        // Играем весь топ, начиная с выбранного трека — как список в Wrapped
        var uris = TopTracks.Select(t => t.Track.Uri).Where(u => !string.IsNullOrEmpty(u)).ToList();
        await _apiClient.PlayTracksAsync(uris, item.Track.Uri);
    }

    [RelayCommand]
    private async Task PlayArtistAsync(RankedArtistItem? artist)
    {
        if (artist == null || string.IsNullOrEmpty(artist.Uri)) return;
        await _apiClient.PlayAsync(contextUri: artist.Uri);
    }

    private bool CanSavePlaylist() => IsLoaded && !IsLoading && TopTracks.Count > 0;

    [RelayCommand(CanExecute = nameof(CanSavePlaylist))]
    private async Task SavePlaylistAsync()
    {
        var period = SelectedPeriod;
        var uris = TopTracks.Select(t => t.Track.Uri).Where(u => !string.IsNullOrEmpty(u)).ToList();
        if (uris.Count == 0) return;

        if (_apiClient.HasScope(Scopes.PlaylistModifyPrivate) == false)
        {
            ShowRelogin();
            return;
        }

        string date = DateTime.Now.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
        string name = $"Мои итоги — {period.Label} ({date})";

        IsLoading = true;
        ClearStatus();
        try
        {
            await _apiClient.CreatePlaylistWithTracksAsync(name,
                $"Топ-{uris.Count} ваших треков за период «{period.Label}». Собрано в Fluent Spotify.",
                isPublic: false, uris);

            // Новый плейлист сразу появляется в медиатеке (меню слева и главная)
            await _main.RefreshPlaylistsCommand.ExecuteAsync(null);

            ShowStatus(InfoBarSeverity.Success, "Плейлист сохранён",
                $"«{name}» — {TextFormat.Tracks(uris.Count)}. Он приватный и уже есть в вашей медиатеке.");
        }
        catch (SpotifyScopeException ex)
        {
            AppLog.Write("WrappedViewModel", ex);
            ShowRelogin();
        }
        catch (Exception ex)
        {
            AppLog.Write("WrappedViewModel", ex);
            ShowStatus(InfoBarSeverity.Error, "Не удалось сохранить плейлист", DescribeError(ex));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply(PeriodData data)
    {
        Clear();

        int rank = 0;
        foreach (var track in data.Tracks)
        {
            TopTracks.Add(new RankedTrackItem { Rank = ++rank, Track = TrackDisplayItem.FromTrack(track) });
        }

        rank = 0;
        foreach (var artist in data.Artists.Take(ArtistsShown))
        {
            TopArtists.Add(new RankedArtistItem
            {
                Rank = ++rank,
                Name = artist.Name ?? string.Empty,
                Uri = artist.Uri ?? string.Empty,
                ImageUrl = artist.Images?.FirstOrDefault()?.Url,
                Genres = string.Join(", ", (artist.Genres ?? new List<string>()).Take(2))
            });
        }

        foreach (var genre in RankGenres(data.Artists).Take(10))
        {
            TopGenres.Add(genre);
        }

        var topTrack = TopTracks.FirstOrDefault();
        TopTrackTitle = topTrack?.Title ?? "—";
        TopTrackArtist = topTrack?.Artist;
        TopTrackImageUrl = topTrack?.ImageUrl;

        var topArtist = TopArtists.FirstOrDefault();
        TopArtistName = topArtist?.Name ?? "—";
        TopArtistImageUrl = topArtist?.ImageUrl;

        var topGenre = TopGenres.FirstOrDefault();
        TopGenreName = topGenre?.Name ?? "Нет данных";
        TopGenreDetails = topGenre?.ArtistsText ?? "Spotify не указал жанры артистов";

        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasGenres));
        OnPropertyChanged(nameof(HasNoGenres));
    }

    /// <summary>
    /// Жанры по артистам топа: чем выше артист, тем больше вес его жанров
    /// (поле genres у Spotify устарело и бывает пустым — тогда список жанров пуст).
    /// </summary>
    private static IEnumerable<GenreItem> RankGenres(IReadOnlyList<FullArtist> artists)
    {
        var weights = new Dictionary<string, (double Weight, List<string> Artists)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < artists.Count; i++)
        {
            double weight = artists.Count - i;
            foreach (var genre in artists[i].Genres ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(genre)) continue;
                if (!weights.TryGetValue(genre, out var entry))
                {
                    entry = (0, new List<string>());
                }
                entry.Artists.Add(artists[i].Name ?? string.Empty);
                weights[genre] = (entry.Weight + weight, entry.Artists);
            }
        }

        if (weights.Count == 0) yield break;

        double max = weights.Values.Max(v => v.Weight);
        int rank = 0;
        foreach (var (genre, value) in weights.OrderByDescending(kv => kv.Value.Weight))
        {
            var names = value.Artists.Take(3).ToList();
            int more = value.Artists.Count - names.Count;
            yield return new GenreItem
            {
                Rank = ++rank,
                Name = CultureInfo.GetCultureInfo("ru-RU").TextInfo.ToTitleCase(genre),
                Share = Math.Round(value.Weight / max * 100),
                ArtistsText = string.Join(", ", names) + (more > 0 ? $" и ещё {more}" : string.Empty)
            };
        }
    }

    private void Clear()
    {
        TopTracks.Clear();
        TopArtists.Clear();
        TopGenres.Clear();
        TopTrackTitle = TopTrackArtist = TopTrackImageUrl = null;
        TopArtistName = TopArtistImageUrl = null;
        TopGenreName = TopGenreDetails = null;
    }

    private void ShowRelogin()
    {
        NeedsRelogin = true;
        StatusSeverity = InfoBarSeverity.Warning;
        StatusTitle = "Нужно войти заново";
        StatusMessage = ReloginMessage;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        NeedsRelogin = false;
        StatusSeverity = severity;
        StatusTitle = title;
        StatusMessage = message;
    }

    private void ClearStatus()
    {
        NeedsRelogin = false;
        StatusTitle = null;
        StatusMessage = null;
    }

    private static string DescribeError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } =>
            "Spotify отклонил запрос (403). Если приложение в режиме разработки, проверьте, что ваш аккаунт " +
            "добавлен в User Management в Spotify Developer Dashboard.",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } =>
            "Слишком много запросов к Spotify. Подождите минуту и нажмите «Обновить».",
        HttpRequestException { StatusCode: null } => "Нет соединения со Spotify. Проверьте интернет и попробуйте ещё раз.",
        _ => ex.Message
    };
}
