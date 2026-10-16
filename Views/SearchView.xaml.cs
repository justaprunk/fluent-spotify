using FluentSpotify.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace FluentSpotify.Views;

public sealed partial class SearchView : Page
{
    public SearchViewModel ViewModel { get; }

    public SearchView()
    {
        this.InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
    }

    private void OnTrackItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackDisplayItem track)
        {
            ViewModel.PlayTrackCommand.Execute(track);
        }
    }
}