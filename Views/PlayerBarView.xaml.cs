using FluentSpotify.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentSpotify.Views;

public sealed partial class PlayerBarView : UserControl
{
    public PlayerBarViewModel? ViewModel => DataContext as PlayerBarViewModel;

    public PlayerBarView()
    {
        InitializeComponent();
        DataContextChanged += (s, e) => Bindings.Update();

        // Slider сам обрабатывает события указателя, поэтому подписываемся с handledEventsToo.
        // Перемотка отправляется в Spotify один раз — когда пользователь отпустил ползунок.
        SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekPointerPressed), true);
        SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekPointerReleased), true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSeekPointerReleased), true);
    }

    private void OnSeekPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel?.BeginSeek();
    }

    private async void OnSeekPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            await vm.EndSeekAsync();
        }
    }
}
