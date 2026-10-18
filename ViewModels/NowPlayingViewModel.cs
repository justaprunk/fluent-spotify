using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentSpotify.Core.Api;
using FluentSpotify.Core.Auth;
using FluentSpotify.Core.NowPlaying;
using FluentSpotify.Core.Settings;
using FluentSpotify.Shared.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using SpotifyAPI.Web;

namespace FluentSpotify.ViewModels;

/// <summary>Строка текста песни в панели: текущая строка ярче остальных.</summary>
public sealed partial class LyricLineItem : ObservableObject
{
    public const double InactiveOpacity = 0.45;

    public TimeSpan? Time { get; init; }
    public string Text { get; init; } = string.Empty;

    [ObservableProperty]
    private double _opacity = 1;
}

/// <summary>Артист трека: роль и подписка (общий объект для блока «Об артисте» и «Авторов»).</summary>
public sealed partial class ArtistCreditItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Uri { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;

    /// <summary>Состояние подписки известно — кнопку можно показывать.</summary>
    [ObservableProperty]
    private bool _canFollow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowText))]
    private bool _isFollowing;

    public string FollowText => Localizer.Get(IsFollowing ? "NowPlaying_Following" : "NowPlaying_Follow");
}

public sealed record QueueItemView(string Title, string Subtitle, string? ImageUrl);

/// <summary>
/// Правая панель «Сейчас играет»: контекст, обложка, «Нравится», текст песни (LRCLIB),
/// артист (Wikidata/Википедия), авторы и очередь. Данные грузятся только при открытой панели.
/// Живёт всё время работы приложения (singleton) — таймер подсветки строк держится здесь.
/// </summary>
public partial class NowPlayingViewModel : ObservableObject
{
    private const int QueuePreviewCount = 3;
    private static readonly TimeSpan LyricsTick = TimeSpan.FromMilliseconds(200);
    // Строку подсвечиваем чуть раньше: позиция из опроса отстаёт от звука
    private static readonly TimeSpan LyricsLead = TimeSpan.FromMilliseconds(250);

    private readonly SpotifyApiClient _api;
    private readonly SettingsService _settings;
    private readonly LyricsService _lyrics;
    private readonly ArtistBioService _bio;
    private readonly IServiceProvider _services;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _lyricsTimer;

    private FullTrack? _track;
    private string? _trackKey;
    private string? _loadedTrackKey;
    private string? _contextUri;
    private string? _loadedContextUri;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _contextCts;

    // Позиция воспроизведения из последнего опроса; между опросами досчитываем по часам
    private long _progressMs;
    private long _progressStamp;
    private bool _isPlaying;

    private bool _showLyrics;
    private bool _showBio;
    private bool _applyingLayout;
    private bool _isLayoutKnown;

    #region Состояние панели

    /// <summary>Панель видна. На широком окне значение запоминается в настройках.</summary>
    [ObservableProperty]
    private bool _isPaneOpen;

    /// <summary>Окно достаточно широкое, чтобы панель стояла рядом с содержимым (иначе — поверх).</summary>
    public bool IsWideLayout { get; private set; }

    #endregion

    #region Заголовок

    [ObservableProperty]
    private bool _hasTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContextTitle))]
    private string? _contextTitle;

    public bool HasContextTitle => !string.IsNullOrEmpty(ContextTitle);

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _artists = string.Empty;

    [ObservableProperty]
    private string? _coverUrl;

    /// <summary>Известно, сохранён ли трек: только тогда показываем кнопку «Нравится».</summary>
    [ObservableProperty]
    private bool _canToggleLike;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeGlyph), nameof(LikeToolTip))]
    private bool _isLiked;

    // Segoe Fluent Icons: EC61 — CompletedSolid (галочка в круге), ECC8 — AddTo (плюс в круге)
    public string LikeGlyph => IsLiked ? "" : "";

    public string LikeToolTip => Localizer.Get(IsLiked ? "NowPlaying_RemoveLiked" : "NowPlaying_AddLiked");

    #endregion

    #region Текст песни

    public ObservableCollection<LyricLineItem> LyricLines { get; } = new();

    [ObservableProperty]
    private bool _hasLyrics;

    [ObservableProperty]
    private bool _isLyricsSynced;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LyricsExpandText))]
    private bool _isLyricsExpanded;

    public string LyricsExpandText => Localizer.Get(IsLyricsExpanded ? "NowPlaying_LyricsCollapse" : "NowPlaying_LyricsExpand");

    /// <summary>Индекс текущей строки синхронизированного текста (-1 — ещё не началась).</summary>
    [ObservableProperty]
    private int _activeLyricIndex = -1;

    #endregion

    #region Артист и авторы

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtist))]
    private ArtistCreditItem? _mainArtist;

    public bool HasArtist => MainArtist != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtistImage))]
    private string? _artistImageUrl;

    public bool HasArtistImage => !string.IsNullOrEmpty(ArtistImageUrl);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtistBio))]
    private string? _artistBio;

    [ObservableProperty]
    private string? _artistBioUrl;

    public bool HasArtistBio => !string.IsNullOrEmpty(ArtistBio);

    public ObservableCollection<ArtistCreditItem> Credits { get; } = new();

    [ObservableProperty]
    private bool _hasCredits;

    #endregion

    #region Очередь

    public ObservableCollection<QueueItemView> NextInQueue { get; } = new();
    public ObservableCollection<QueueItemView> FullQueue { get; } = new();

    [ObservableProperty]
    private bool _hasQueue;

    #endregion

    /// <summary>Токен выдан до появления панели: для «Нравится» и подписок нужен повторный вход.</summary>
    [ObservableProperty]
    private bool _needsRelogin;

    // Подсказку закрыли — не показываем её заново при каждой смене трека
    private bool _reloginDismissed;

    partial void OnNeedsReloginChanged(bool oldValue, bool newValue)
    {
        if (oldValue && !newValue) _reloginDismissed = true;
    }

    public NowPlayingViewModel(SpotifyApiClient api, SettingsService settings, LyricsService lyrics,
        ArtistBioService bio, IServiceProvider services)
    {
        _api = api;
        _settings = settings;
        _lyrics = lyrics;
        _bio = bio;
        _services = services;
        _showLyrics = settings.Current.ShowLyrics;
        _showBio = settings.Current.ShowArtistBio;

        // Создаётся в UI-потоке (через PlayerBarViewModel → MainWindow); всё состояние меняем только в нём
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _lyricsTimer = _dispatcher.CreateTimer();
        _lyricsTimer.Interval = LyricsTick;
        _lyricsTimer.Tick += (s, e) => UpdateActiveLyric();

        _api.PlaybackUpdated += playback => RunOnUi(() => ApplyPlayback(playback));
        _settings.Changed += s => RunOnUi(() => OnSettingsChanged(s));
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    #region Раскладка окна

    /// <summary>Окно сообщает ширину: на узком панель закрывается и открывается только поверх содержимого.</summary>
    public void ApplyLayout(bool wide)
    {
        if (_isLayoutKnown && wide == IsWideLayout) return;

        _isLayoutKnown = true;
        IsWideLayout = wide;
        _applyingLayout = true;
        try
        {
            IsPaneOpen = wide && _settings.Current.NowPlayingPaneOpen;
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    partial void OnIsPaneOpenChanged(bool value)
    {
        // Запоминаем только выбор на широком окне: на узком панель закрывается сама
        if (!_applyingLayout && IsWideLayout && _settings.Current.NowPlayingPaneOpen != value)
        {
            _settings.Update(s => s.NowPlayingPaneOpen = value, notify: false);
        }

        if (value)
        {
            if (_loadedTrackKey != _trackKey) _ = LoadTrackAsync();
            else if (_loadedContextUri != _contextUri) _ = LoadContextAsync();
        }
        UpdateLyricsTimer();
    }

    [RelayCommand]
    private void TogglePane() => IsPaneOpen = !IsPaneOpen;

    #endregion

    #region Воспроизведение

    private void ApplyPlayback(CurrentlyPlayingContext? playback)
    {
        var track = playback?.Item as FullTrack;
        _isPlaying = playback?.IsPlaying ?? false;
        _progressMs = playback?.ProgressMs ?? 0;
        _progressStamp = Stopwatch.GetTimestamp();

        // У локальных файлов нет id — различаем по URI
        string? key = track == null ? null : track.Id ?? track.Uri;
        string? contextUri = track == null ? null : playback?.Context?.Uri;

        if (key != _trackKey)
        {
            _trackKey = key;
            _track = track;
            _contextUri = contextUri;
            if (IsPaneOpen) _ = LoadTrackAsync();
        }
        else if (contextUri != _contextUri)
        {
            _contextUri = contextUri;
            if (IsPaneOpen) _ = LoadContextAsync();
        }

        UpdateActiveLyric();
        UpdateLyricsTimer();
    }

    private void OnSettingsChanged(AppSettings s)
    {
        bool lyricsChanged = s.ShowLyrics != _showLyrics;
        bool bioChanged = s.ShowArtistBio != _showBio;
        _showLyrics = s.ShowLyrics;
        _showBio = s.ShowArtistBio;
        if (!lyricsChanged && !bioChanged) return;

        if (!_showLyrics) ClearLyrics();
        if (!_showBio) ArtistBio = ArtistBioUrl = null;

        // Включили обратно — догружаем при открытой панели, иначе при следующем открытии
        if (_track != null && (_showLyrics && lyricsChanged || _showBio && bioChanged))
        {
            if (IsPaneOpen) _ = LoadTrackAsync();
            else _loadedTrackKey = null;
        }
    }

    #endregion

    #region Загрузка

    private async Task LoadTrackAsync()
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var ct = cts.Token;

        var track = _track;
        _loadedTrackKey = _trackKey;
        ClearTrack();
        if (track == null)
        {
            _loadedContextUri = _contextUri;
            ContextTitle = null;
            return;
        }

        HasTrack = true;
        Title = track.Name;
        Artists = string.Join(", ", track.Artists.Select(a => a.Name));
        CoverUrl = track.Album?.Images?.FirstOrDefault()?.Url;

        for (int i = 0; i < track.Artists.Count; i++)
        {
            var artist = track.Artists[i];
            if (string.IsNullOrEmpty(artist.Name)) continue;
            Credits.Add(new ArtistCreditItem
            {
                Id = artist.Id ?? string.Empty,
                Uri = artist.Uri ?? string.Empty,
                Name = artist.Name,
                Role = Localizer.Get(IsFeatured(track.Name, artist.Name, i) ? "NowPlaying_RoleFeatured" : "NowPlaying_RoleMain")
            });
        }
        HasCredits = Credits.Count > 0;
        MainArtist = Credits.FirstOrDefault(c => c.Id.Length > 0);

        // Если токен заведомо без новых скоупов — сразу предлагаем войти заново
        if (PkceAuthService.NowPlayingScopes.Any(scope => _api.HasScope(scope) == false))
        {
            if (!_reloginDismissed) NeedsRelogin = true;
        }

        // Секции независимы: ошибка одной не мешает остальным, пустые просто скрыты.
        // Продолжения после await возвращаются в UI-поток (контекст синхронизации XAML).
        await Task.WhenAll(
            LoadContextAsync(),
            LoadLibraryStateAsync(track, ct),
            LoadArtistAsync(ct),
            LoadLyricsAsync(track, ct),
            LoadQueueAsync(ct));
    }

    private async Task LoadContextAsync()
    {
        _contextCts?.Cancel();
        var cts = new CancellationTokenSource();
        _contextCts = cts;
        string? uri = _contextUri;
        _loadedContextUri = uri;

        if (string.IsNullOrEmpty(uri))
        {
            ContextTitle = null;
            return;
        }
        if (uri.EndsWith(":collection", StringComparison.Ordinal))
        {
            ContextTitle = Localizer.Get("Nav_Liked");
            return;
        }

        try
        {
            string? name = await _api.GetContextNameAsync(uri, cts.Token);
            if (!cts.IsCancellationRequested) ContextTitle = name;
        }
        catch (Exception ex) when (!cts.IsCancellationRequested)
        {
            // Например, чужой плейлист, недоступный в Development Mode, — просто без заголовка
            AppLog.Write("NowPlaying", $"Context: {ex.Message}");
            ContextTitle = null;
        }
        catch (Exception)
        {
        }
    }

    private async Task LoadLibraryStateAsync(FullTrack track, CancellationToken ct)
    {
        // Трек и артисты — разными запросами: у них разные скоупы (user-library-read и user-follow-read),
        // и без нового скоупа отметка «Нравится» всё равно должна работать
        try
        {
            if (!string.IsNullOrEmpty(track.Uri))
            {
                var liked = await _api.LibraryContainsAsync([track.Uri], ct);
                if (ct.IsCancellationRequested) return;
                IsLiked = liked.Count > 0 && liked[0];
                CanToggleLike = liked.Count > 0;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            HandleSectionError("Library", ex);
        }
        catch (Exception)
        {
            return;
        }

        var artists = Credits.Where(c => c.Uri.Length > 0).ToList();
        if (artists.Count == 0) return;
        try
        {
            var following = await _api.LibraryContainsAsync(artists.Select(a => a.Uri).ToList(), ct);
            if (ct.IsCancellationRequested) return;
            for (int i = 0; i < artists.Count && i < following.Count; i++)
            {
                artists[i].IsFollowing = following[i];
                artists[i].CanFollow = true;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            HandleSectionError("Follow", ex);
        }
        catch (Exception)
        {
        }
    }

    private async Task LoadArtistAsync(CancellationToken ct)
    {
        if (MainArtist is not { } artist) return;

        try
        {
            var details = await _api.GetArtistAsync(artist.Id, ct);
            if (ct.IsCancellationRequested) return;
            ArtistImageUrl = details?.ImageUrl;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            HandleSectionError("Artist", ex);
        }
        catch (Exception)
        {
            return;
        }

        if (!_showBio) return;
        try
        {
            var bio = await _bio.GetAsync(artist.Id, ct);
            if (ct.IsCancellationRequested || !_showBio) return;
            ArtistBio = bio?.Text;
            ArtistBioUrl = bio?.PageUrl;
        }
        catch (Exception ex) when (ex is OperationCanceledException || ct.IsCancellationRequested)
        {
        }
    }

    private async Task LoadLyricsAsync(FullTrack track, CancellationToken ct)
    {
        if (!_showLyrics || string.IsNullOrEmpty(track.Name) || track.Artists.Count == 0) return;

        try
        {
            var lyrics = await _lyrics.GetAsync(track.Id ?? track.Uri, track.Artists[0].Name, track.Name,
                track.Album?.Name, TimeSpan.FromMilliseconds(track.DurationMs), ct);
            if (ct.IsCancellationRequested || !_showLyrics || lyrics == null || lyrics.Lines.Count == 0) return;

            foreach (var line in lyrics.Lines)
            {
                LyricLines.Add(new LyricLineItem
                {
                    Time = line.Time,
                    Text = line.Text.Length > 0 ? line.Text : "♪",
                    Opacity = lyrics.IsSynced ? LyricLineItem.InactiveOpacity : 1
                });
            }
            IsLyricsSynced = lyrics.IsSynced;
            HasLyrics = true;
            UpdateActiveLyric();
            UpdateLyricsTimer();
        }
        catch (Exception ex) when (ex is OperationCanceledException || ct.IsCancellationRequested)
        {
        }
    }

    private async Task LoadQueueAsync(CancellationToken ct)
    {
        try
        {
            var queue = await _api.GetQueueAsync(ct);
            if (ct.IsCancellationRequested) return;

            FullQueue.Clear();
            NextInQueue.Clear();
            foreach (var entry in queue)
            {
                var item = new QueueItemView(entry.Title, entry.Subtitle, entry.ImageUrl);
                FullQueue.Add(item);
                if (NextInQueue.Count < QueuePreviewCount) NextInQueue.Add(item);
            }
            HasQueue = FullQueue.Count > 0;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            HandleSectionError("Queue", ex);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Перед показом полной очереди — свежие данные (её меняют и с других устройств).</summary>
    [RelayCommand]
    private Task RefreshQueueAsync() => LoadQueueAsync(_loadCts?.Token ?? CancellationToken.None);

    /// <param name="userAction">
    /// Ошибка после нажатия кнопки — подсказку о входе показываем, даже если её уже закрывали.
    /// </param>
    private void HandleSectionError(string section, Exception ex, bool userAction = false)
    {
        AppLog.Write("NowPlaying", $"{section}: {ex.Message}");
        if (ex is SpotifyScopeException && (userAction || !_reloginDismissed)) NeedsRelogin = true;
    }

    private void ClearTrack()
    {
        HasTrack = false;
        Title = Artists = string.Empty;
        CoverUrl = null;
        CanToggleLike = false;
        IsLiked = false;
        MainArtist = null;
        ArtistImageUrl = null;
        ArtistBio = ArtistBioUrl = null;
        Credits.Clear();
        HasCredits = false;
        NextInQueue.Clear();
        FullQueue.Clear();
        HasQueue = false;
        ClearLyrics();
    }

    private void ClearLyrics()
    {
        LyricLines.Clear();
        HasLyrics = false;
        IsLyricsSynced = false;
        IsLyricsExpanded = false;
        ActiveLyricIndex = -1;
        UpdateLyricsTimer();
    }

    #endregion

    #region Действия

    [RelayCommand]
    private async Task ToggleLikeAsync()
    {
        if (_track?.Uri is not { Length: > 0 } uri || !CanToggleLike) return;

        bool target = !IsLiked;
        IsLiked = target;
        try
        {
            if (target) await _api.SaveToLibraryAsync(uri);
            else await _api.RemoveFromLibraryAsync(uri);
        }
        catch (Exception ex)
        {
            // Возвращаем прежнее состояние, если трек за это время не сменился
            if (_track?.Uri == uri) IsLiked = !target;
            HandleSectionError("Like", ex, userAction: true);
        }
    }

    [RelayCommand]
    private async Task ToggleFollowAsync(ArtistCreditItem? artist)
    {
        if (artist == null || artist.Uri.Length == 0 || !artist.CanFollow) return;

        bool target = !artist.IsFollowing;
        artist.IsFollowing = target;
        try
        {
            if (target) await _api.SaveToLibraryAsync(artist.Uri);
            else await _api.RemoveFromLibraryAsync(artist.Uri);
        }
        catch (Exception ex)
        {
            artist.IsFollowing = !target;
            HandleSectionError("Follow", ex, userAction: true);
        }
    }

    [RelayCommand]
    private void ToggleLyricsExpanded() => IsLyricsExpanded = !IsLyricsExpanded;

    [RelayCommand]
    private void OpenArtistBio()
    {
        if (ArtistBioUrl is not { Length: > 0 } url) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write("NowPlaying", ex);
        }
    }

    [RelayCommand]
    private async Task ReloginAsync()
    {
        // Пока команда выполняется, кнопка неактивна (AsyncRelayCommand)
        try
        {
            await _services.GetRequiredService<MainViewModel>().ReauthenticateAsync();
            NeedsRelogin = false;
            _reloginDismissed = false;
            _loadedTrackKey = null;
            if (IsPaneOpen) await LoadTrackAsync();
        }
        catch (Exception ex)
        {
            // Вход отменён или не удался — текущая сессия остаётся, подсказка тоже
            AppLog.Write("NowPlaying", ex);
        }
    }

    #endregion

    #region Подсветка строк

    private void UpdateLyricsTimer()
    {
        bool needed = IsPaneOpen && _isPlaying && IsLyricsSynced && HasLyrics;
        if (needed && !_lyricsTimer.IsRunning) _lyricsTimer.Start();
        else if (!needed && _lyricsTimer.IsRunning) _lyricsTimer.Stop();
    }

    private void UpdateActiveLyric()
    {
        if (!IsLyricsSynced || LyricLines.Count == 0) return;

        long elapsed = _isPlaying ? (long)Stopwatch.GetElapsedTime(_progressStamp).TotalMilliseconds : 0;
        var position = TimeSpan.FromMilliseconds(_progressMs + elapsed) + LyricsLead;

        // Последняя строка, которая уже началась (строки отсортированы по времени)
        int index = -1;
        int lo = 0, hi = LyricLines.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (LyricLines[mid].Time <= position)
            {
                index = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (index == ActiveLyricIndex) return;
        if (ActiveLyricIndex >= 0 && ActiveLyricIndex < LyricLines.Count)
        {
            LyricLines[ActiveLyricIndex].Opacity = LyricLineItem.InactiveOpacity;
        }
        if (index >= 0) LyricLines[index].Opacity = 1;
        ActiveLyricIndex = index;
    }

    #endregion

    /// <summary>
    /// Роль артиста: первый — основной; остальные — приглашённые, если названы в «(feat. …)»
    /// или «(with …)» в названии трека, иначе тоже основные (совместный трек).
    /// </summary>
    private static bool IsFeatured(string trackName, string artistName, int index)
    {
        if (index == 0) return false;
        var match = FeatRegex().Match(trackName);
        return match.Success && match.Groups[1].Value.Contains(artistName, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[\(\[\-–]\s*(?:feat\.?|ft\.?|featuring|with)\s+([^\)\]]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FeatRegex();
}
