using FluentSpotify.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

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

    private async void OnExportFoldersClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "fluent-spotify-folders"
        };
        picker.FileTypeChoices.Add(Localizer.Get("Folders_FileType"), new List<string> { ".json" });
        InitializeWithWindow.Initialize(picker, MainWindowHandle);

        if (await picker.PickSaveFileAsync() is { } file)
        {
            ViewModel.ExportFolders(file.Path);
        }
    }

    private async void OnImportFoldersClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, MainWindowHandle);

        if (await picker.PickSingleFileAsync() is not { } file) return;

        // Импорт заменяет текущие папки — спрашиваем, если терять есть что
        if (ViewModel.Folders.HasFolders)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
                RequestedTheme = ActualTheme,
                Title = Localizer.Get("Folders_ImportConfirmTitle"),
                Content = Localizer.Get("Folders_ImportConfirmMessage"),
                PrimaryButtonText = Localizer.Get("Folders_Replace"),
                CloseButtonText = Localizer.Get("Dialog_Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        ViewModel.ImportFolders(file.Path);
    }

    private static nint MainWindowHandle => WindowNative.GetWindowHandle(App.Services.GetRequiredService<MainWindow>());
}