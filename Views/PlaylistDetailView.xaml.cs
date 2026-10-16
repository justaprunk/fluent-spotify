using FluentSpotify.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentSpotify.Views;

public sealed partial class PlaylistDetailView : Page
{
    public PlaylistViewModel ViewModel { get; }

    public PlaylistDetailView()
    {
        ViewModel = App.Services.GetRequiredService<PlaylistViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string playlistId)
        {
            await ViewModel.LoadAsync(playlistId);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.CancelLoading();
    }

    private void OnTrackItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackDisplayItem track)
        {
            ViewModel.PlayTrackCommand.Execute(track);
        }
    }
}
