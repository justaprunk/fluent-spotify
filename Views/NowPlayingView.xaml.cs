using System.ComponentModel;
using FluentSpotify.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace FluentSpotify.Views;

/// <summary>Правая панель «Сейчас играет». Создаётся один раз вместе с главным окном.</summary>
public sealed partial class NowPlayingView : UserControl
{
    private const double LyricsPreviewHeight = 200;
    private const double LyricsExpandedHeight = 480;
    // Текущая строка — примерно на трети высоты окна текста, как в официальном клиенте
    private const double ActiveLineRatio = 0.3;

    public NowPlayingViewModel ViewModel { get; }

    public NowPlayingView()
    {
        ViewModel = App.Services.GetRequiredService<NowPlayingViewModel>();
        InitializeComponent();

        // View-модель и окно живут всё время работы приложения, отписка не нужна
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Панель поверх содержимого (узкое окно): свой фон и отступ слева.</summary>
    public void SetOverlayMode(bool overlay)
    {
        OverlayBackground.Visibility = overlay ? Visibility.Visible : Visibility.Collapsed;
        RootScroll.Padding = overlay ? new Thickness(12, 12, 12, 12) : new Thickness(0, 0, 12, 12);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NowPlayingViewModel.ActiveLyricIndex):
                ScrollToActiveLyric(animate: true);
                break;
            case nameof(NowPlayingViewModel.IsLyricsExpanded):
                LyricsScroll.Height = ViewModel.IsLyricsExpanded ? LyricsExpandedHeight : LyricsPreviewHeight;
                // Дождёмся новой высоты, чтобы текущая строка встала на своё место
                DispatcherQueue.TryEnqueue(() => ScrollToActiveLyric(animate: false));
                break;
            case nameof(NowPlayingViewModel.HasLyrics) when !ViewModel.HasLyrics:
                LyricsScroll.ChangeView(null, 0, null, true);
                break;
        }
    }

    private void ScrollToActiveLyric(bool animate, bool retry = true)
    {
        int index = ViewModel.ActiveLyricIndex;
        if (index < 0)
        {
            LyricsScroll.ChangeView(null, 0, null, !animate);
            return;
        }

        // Текст только что загружен — строки ещё не созданы; повторяем после разметки
        if (LyricsList.ContainerFromIndex(index) is not FrameworkElement line)
        {
            if (retry)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ScrollToActiveLyric(false, retry: false));
            }
            return;
        }

        // StartBringIntoView прокрутил бы и всю панель — двигаем только окно текста
        double top = line.TransformToVisual(LyricsList).TransformPoint(new Point(0, 0)).Y;
        double offset = Math.Max(0, top - LyricsScroll.ViewportHeight * ActiveLineRatio);
        LyricsScroll.ChangeView(null, offset, null, !animate);
    }

    private void OnCoverSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Квадрат по ширине панели
        if (double.IsNaN(CoverBorder.Height) || Math.Abs(e.NewSize.Width - CoverBorder.Height) > 0.5)
        {
            CoverBorder.Height = e.NewSize.Width;
        }
    }

    private void OnFollowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ArtistCreditItem artist)
        {
            ViewModel.ToggleFollowCommand.Execute(artist);
        }
    }

    private void OnOpenQueueClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshQueueCommand.Execute(null);
        FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
    }
}
