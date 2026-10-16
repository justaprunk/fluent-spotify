using FluentSpotify.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentSpotify.Views;

public sealed partial class WrappedView : Page
{
    // Уже этой ширины страницы карточки и колонка жанров не помещаются рядом
    private const double WideLayoutMinWidth = 860;

    public WrappedViewModel ViewModel { get; }

    public WrappedView()
    {
        ViewModel = App.Services.GetRequiredService<WrappedViewModel>();
        InitializeComponent();

        foreach (var period in ViewModel.Periods)
        {
            PeriodSelector.Items.Add(new SelectorBarItem { Text = period.Label, Tag = period });
        }
        PeriodSelector.SelectedItem = PeriodSelector.Items[0];
        PeriodSelector.SelectionChanged += OnPeriodSelectionChanged;

        SizeChanged += (s, e) => UpdateLayoutState(e.NewSize.Width);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.CancelLoading();
    }

    private void UpdateLayoutState(double width)
    {
        VisualStateManager.GoToState(this, width >= WideLayoutMinWidth ? "Wide" : "Narrow", false);
    }

    private void OnPeriodSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is WrappedPeriod period)
        {
            ViewModel.SelectedPeriod = period;
        }
    }

    private void OnTrackItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RankedTrackItem track)
        {
            ViewModel.PlayTrackCommand.Execute(track);
        }
    }

    private void OnArtistItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RankedArtistItem artist)
        {
            ViewModel.PlayArtistCommand.Execute(artist);
        }
    }
}
