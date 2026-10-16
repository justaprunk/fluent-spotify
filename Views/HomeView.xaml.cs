using FluentSpotify.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SpotifyAPI.Web;

namespace FluentSpotify.Views;

public sealed partial class HomeView : Page
{
    public MainViewModel ViewModel { get; }

    public HomeView()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        InitializeComponent();
    }

    private void OnLikedSongsClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Frame.Navigate(typeof(PlaylistDetailView), FluentSpotify.Core.Api.SpotifyApiClient.LikedSongsId);
    }

    private void OnPlaylistItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FullPlaylist { Id: { Length: > 0 } id })
        {
            Frame.Navigate(typeof(PlaylistDetailView), id);
        }
    }
}
