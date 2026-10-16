using FluentSpotify.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentSpotify.Views;

public sealed partial class SettingsView : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsView()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();

        Loaded += (s, e) =>
        {
            ViewModel.Updates.DetailsRequested += OnUpdateDetailsRequested;
            OnUpdateDetailsRequested();
        };
        Unloaded += (s, e) => ViewModel.Updates.DetailsRequested -= OnUpdateDetailsRequested;
    }

    /// <summary>«Подробнее» в InfoBar: прокрутка к найденной версии с описанием изменений.</summary>
    private void OnUpdateDetailsRequested()
    {
        if (!ViewModel.Updates.TakeScrollToDetails()) return;

        // После раскрытия Expander разметка ещё не пересчитана — прокручиваем следующим проходом
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            UpdatesExpander.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.1 }));
    }
}
