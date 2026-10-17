using FluentSpotify.Core.Library;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpotifyAPI.Web;
using Microsoft.UI.Input;
using Microsoft.UI.Input.DragDrop;
using Microsoft.UI.Content;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace FluentSpotify;

/// <summary>
/// «Моя медиатека» в боковой панели: локальные папки (раскрываемые пункты) и плейлисты,
/// контекстные меню и перетаскивание. Пункты строятся из кода: MenuItems и MenuItemsSource
/// нельзя использовать вместе.
/// </summary>
public sealed partial class MainWindow
{
    private const string PlaylistGlyph = "";
    private const string FolderGlyph = "";

    private readonly List<NavigationViewItem> _libraryItems = new();
    private string? _selectedPlaylistId;
    private bool _restoringSelection;
    private bool _rebuilding;

    /// <summary>Что сейчас перетаскивается: плейлист или папка.</summary>
    private DragItem? _drag;

    private sealed record FolderTag(string Id);

    private sealed record DragItem(string? PlaylistId, PlaylistFolder? Folder);

    private PlaylistFolderService Folders => ViewModel.Folders;

    /// <summary>
    /// Держим DragDropManager окна, пока живо окно. XAML хранит ссылки на него только во временных
    /// объектах перетаскивания; когда сборщик мусора финализирует их обёртки, последняя ссылка
    /// освобождалась в потоке финализатора, а DragDropManager закрывается только в своём потоке —
    /// исключение там завершало процесс (0xc0000409 в ucrtbase.dll, 1.3.0).
    /// </summary>
    private readonly List<object> _dragDropManagers = new();

    private void PinDragDropManager()
    {
        try
        {
            // Остров с тем же DragDropManager держим тоже: иначе его обёртку финализирует тот же поток
            foreach (var island in ContentIsland.FindAllForCurrentThread())
            {
                if (DragDropManager.GetForIsland(island) is { } manager && !_dragDropManagers.Contains(manager))
                {
                    _dragDropManagers.Add(island);
                    _dragDropManagers.Add(manager);
                }
            }
            Shared.Diagnostics.AppLog.Write("Folders", $"DragDropManager закреплён: {_dragDropManagers.Count / 2}");
            if (_dragDropManagers.Count == 0)
            {
                Shared.Diagnostics.AppLog.Write("Folders", "DragDropManager недоступен — перетаскивание может завершить приложение");
            }
        }
        catch (Exception ex)
        {
            Shared.Diagnostics.AppLog.Write("Folders", ex);
        }
    }

    private void SetupLibraryHeader()
    {
        // Окно уже показано — у потока есть остров контента и его DragDropManager
        RootGrid.Loaded += (s, e) => PinDragDropManager();

        // Стиль WinUI выключает заголовок (IsEnabled = false), а у корня шаблона нет фона —
        // такой элемент не получает ни меню, ни бросок. Включаем и даём прозрачный фон.
        LibraryHeader.IsEnabled = true;
        LibraryHeader.Loaded += (s, e) =>
        {
            if (VisualTreeHelper.GetChildrenCount(LibraryHeader) > 0 && VisualTreeHelper.GetChild(LibraryHeader, 0) is Panel { Background: null } root)
            {
                root.Background = new SolidColorBrush(Colors.Transparent);
            }
        };

        LibraryHeader.ContextFlyout = CreateMenu(menu =>
            menu.Items.Add(MenuItem("Folder_New", "\uE8F4", () => _ = CreateFolderAsync(Folders.Root))));

        // Бросок на заголовок — убрать из папки в корень медиатеки
        LibraryHeader.AllowDrop = true;
        LibraryHeader.DragOver += (s, e) => AcceptDrop(e, CanDropInto(Folders.Root), Localizer.Format("Folder_DragInto", Localizer.Get("Nav_Library")));
        LibraryHeader.Drop += (s, e) => DropInto(e, Folders.Root);
    }
    /// <summary>Перестраивает папки и плейлисты; выбранный плейлист остаётся выбранным.</summary>
    private void RebuildLibrary()
    {
        _rebuilding = true;
        try
        {
            foreach (var item in _libraryItems)
            {
                NavView.MenuItems.Remove(item);
            }
            _libraryItems.Clear();

            NavigationViewItem? selected = null;
            var playlists = ViewModel.Playlists.Where(p => p.Id != null).ToDictionary(p => p.Id!);
            AddChildren(_libraryItems, Folders.Root, playlists, ref selected);
            foreach (var item in _libraryItems)
            {
                NavView.MenuItems.Add(item);
            }

            if (selected != null)
            {
                _restoringSelection = true;
                NavView.SelectedItem = selected;
            }
        }
        finally
        {
            _restoringSelection = false;
            _rebuilding = false;
        }
    }

    private void AddChildren(IList<NavigationViewItem> target, PlaylistFolder folder,
        Dictionary<string, FullPlaylist> playlists, ref NavigationViewItem? selected)
    {
        foreach (var child in folder.Folders)
        {
            var item = CreateFolderItem(child);
            var children = new List<NavigationViewItem>();
            AddChildren(children, child, playlists, ref selected);
            foreach (var nested in children)
            {
                item.MenuItems.Add(nested);
            }
            target.Add(item);
        }

        foreach (string id in folder.Playlists)
        {
            if (!playlists.TryGetValue(id, out var playlist)) continue;
            var item = CreatePlaylistItem(playlist);
            if (id == _selectedPlaylistId) selected = item;
            target.Add(item);
        }
    }

    private NavigationViewItem CreateFolderItem(PlaylistFolder folder)
    {
        int count = folder.TotalPlaylists;
        var item = new NavigationViewItem
        {
            Content = CreateFolderLabel(folder.Name, count),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = new FolderTag(folder.Id),
            Icon = new FontIcon { Glyph = FolderGlyph },
            // Нажатие раскрывает папку, а не открывает страницу
            SelectsOnInvoked = false,
            IsExpanded = folder.IsExpanded,
            CanDrag = true,
            AllowDrop = true
        };
        string summary = $"{folder.Name} • {Localizer.Plural("Playlists", count)}";
        ToolTipService.SetToolTip(item, summary);
        AutomationProperties.SetName(item, summary);

        item.ContextFlyout = CreateMenu(menu => FillFolderMenu(menu, folder));
        EnableDragStart(item);
        item.DragStarting += (s, e) => StartDrag(e, new DragItem(null, folder), folder.Name);
        item.DropCompleted += (s, e) => _drag = null;
        item.DragOver += (s, e) => AcceptDrop(e, CanDropInto(folder), Localizer.Format("Folder_DragInto", folder.Name));
        item.Drop += (s, e) => DropInto(e, folder);
        return item;
    }

    private NavigationViewItem CreatePlaylistItem(FullPlaylist playlist)
    {
        string id = playlist.Id!;
        var item = new NavigationViewItem
        {
            Content = playlist.Name,
            Tag = playlist,
            Icon = new FontIcon { Glyph = PlaylistGlyph },
            CanDrag = true,
            AllowDrop = true
        };

        item.ContextFlyout = CreateMenu(menu => FillPlaylistMenu(menu, id));
        EnableDragStart(item);
        item.DragStarting += (s, e) => StartDrag(e, new DragItem(id, null), playlist.Name ?? string.Empty);
        item.DropCompleted += (s, e) => _drag = null;
        // Плейлист, брошенный на плейлист, встаёт перед ним (в той же папке)
        item.DragOver += (s, e) => AcceptDrop(e, _drag?.PlaylistId is { } dragged && dragged != id,
            Localizer.Format("Folder_DragBefore", playlist.Name));
        item.Drop += (s, e) =>
        {
            if (_drag?.PlaylistId is not { } dragged) return;
            e.Handled = true;
            _drag = null;
            Defer(() =>
            {
                if (Folders.FindPlaylistFolder(id) is { } folder) Folders.MovePlaylist(dragged, folder, before: id);
            });
        };
        return item;
    }

    /// <summary>Название папки и справа — число плейлистов приглушённым цветом.</summary>
    private static Grid CreateFolderLabel(string name, int count)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var counter = new TextBlock
        {
            Text = count.ToString(Localizer.Culture),
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(counter, 1);
        grid.Children.Add(title);
        grid.Children.Add(counter);
        return grid;
    }

    #region Раскрытие папок

    private void NavView_Expanding(NavigationView sender, NavigationViewItemExpandingEventArgs args) =>
        RememberExpanded(args.ExpandingItemContainer, true);

    private void NavView_Collapsed(NavigationView sender, NavigationViewItemCollapsedEventArgs args) =>
        RememberExpanded(args.CollapsedItemContainer, false);

    private void RememberExpanded(NavigationViewItemBase? item, bool expanded)
    {
        // В свёрнутой панели (Compact) папка раскрывается всплывающим списком — это не её состояние
        bool inlinePane = NavView.DisplayMode == NavigationViewDisplayMode.Expanded || NavView.IsPaneOpen;
        if (_rebuilding || !inlinePane || item?.Tag is not FolderTag tag) return;
        Folders.SetExpanded(tag.Id, expanded);
    }

    #endregion

    #region Контекстные меню

    private void FillFolderMenu(MenuFlyout menu, PlaylistFolder folder)
    {
        menu.Items.Add(MenuItem("Folder_NewSubfolder", "", () => _ = CreateFolderAsync(folder)));
        menu.Items.Add(MenuItem("Folder_Rename", "", () => _ = RenameFolderAsync(folder)));
        menu.Items.Add(CreateMoveToMenu(
            current: Folders.FindParent(folder),
            canTarget: target => Folders.CanMoveFolder(folder, target),
            move: target => Folders.MoveFolder(folder, target)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("Folder_MoveUp", "", () => Folders.ShiftFolder(folder, -1), Folders.CanShiftFolder(folder, -1)));
        menu.Items.Add(MenuItem("Folder_MoveDown", "", () => Folders.ShiftFolder(folder, 1), Folders.CanShiftFolder(folder, 1)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("Folder_Delete", "", () => Folders.Delete(folder)));
    }

    private void FillPlaylistMenu(MenuFlyout menu, string playlistId)
    {
        var moveTo = CreateMoveToMenu(
            current: Folders.FindPlaylistFolder(playlistId),
            canTarget: _ => true,
            move: target => Folders.MovePlaylist(playlistId, target));
        moveTo.Items.Add(new MenuFlyoutSeparator());
        moveTo.Items.Add(MenuItem("Folder_NewWithItem", "", () => _ = CreateFolderAsync(Folders.Root, playlistId)));
        menu.Items.Add(moveTo);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("Folder_MoveUp", "", () => Folders.ShiftPlaylist(playlistId, -1), Folders.CanShiftPlaylist(playlistId, -1)));
        menu.Items.Add(MenuItem("Folder_MoveDown", "", () => Folders.ShiftPlaylist(playlistId, 1), Folders.CanShiftPlaylist(playlistId, 1)));
    }

    /// <summary>«Переместить в папку»: корень медиатеки и все папки лесенкой; текущее место недоступно.</summary>
    private MenuFlyoutSubItem CreateMoveToMenu(PlaylistFolder? current, Func<PlaylistFolder, bool> canTarget, Action<PlaylistFolder> move)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = Localizer.Get("Folder_MoveTo"),
            Icon = new FontIcon { Glyph = "" }
        };

        void AddTarget(PlaylistFolder target, string name, string glyph, int depth)
        {
            var item = MenuItem(name, glyph, () => move(target), target != current && canTarget(target), localize: false);
            item.Padding = new Thickness(11 + depth * 16, 8, 11, 9);
            menu.Items.Add(item);
        }

        AddTarget(Folders.Root, Localizer.Get("Nav_Library"), "", 0);
        foreach (var (folder, depth) in Folders.Flatten())
        {
            AddTarget(folder, folder.Name, FolderGlyph, depth + 1);
        }
        return menu;
    }

    /// <summary>Меню заполняется при каждом открытии — так в нём всегда актуальный список папок.</summary>
    private static MenuFlyout CreateMenu(Action<MenuFlyout> fill)
    {
        var menu = new MenuFlyout();
        menu.Opening += (s, e) =>
        {
            menu.Items.Clear();
            fill(menu);
        };
        return menu;
    }

    private MenuFlyoutItem MenuItem(string text, string glyph, Action action, bool enabled = true, bool localize = true)
    {
        var item = new MenuFlyoutItem
        {
            Text = localize ? Localizer.Get(text) : text,
            Icon = new FontIcon { Glyph = glyph },
            IsEnabled = enabled
        };
        // Перестраиваем панель после закрытия меню, а не посреди его обработчика
        item.Click += (s, e) => Defer(action);
        return item;
    }

    private void Defer(Action action) => DispatcherQueue.TryEnqueue(() => action());

    #endregion

    #region Диалоги

    /// <param name="playlistId">Плейлист, который сразу переносится в новую папку.</param>
    private async Task CreateFolderAsync(PlaylistFolder parent, string? playlistId = null)
    {
        string? name = await AskFolderNameAsync(Localizer.Get("Folder_New"), Localizer.Get("Folder_Create"),
            Localizer.Get("Folder_DefaultName"));
        if (name == null) return;

        var folder = Folders.CreateFolder(parent, name);
        if (playlistId != null)
        {
            Folders.MovePlaylist(playlistId, folder);
        }
    }

    private async Task RenameFolderAsync(PlaylistFolder folder)
    {
        string? name = await AskFolderNameAsync(Localizer.Get("Folder_RenameTitle"), Localizer.Get("Dialog_Save"), folder.Name);
        if (name != null && name != folder.Name)
        {
            Folders.Rename(folder, name);
        }
    }

    /// <returns>Введённое название или null, если пользователь отменил.</returns>
    private async Task<string?> AskFolderNameAsync(string title, string primaryText, string initial)
    {
        var box = new TextBox
        {
            Text = initial,
            PlaceholderText = Localizer.Get("Folder_NamePlaceholder"),
            MaxLength = PlaylistFolderService.MaxNameLength
        };
        box.Loaded += (s, e) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            RequestedTheme = RootGrid.ActualTheme,
            Title = title,
            Content = box,
            PrimaryButtonText = primaryText,
            CloseButtonText = Localizer.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = initial.Trim().Length > 0
        };
        box.TextChanged += (s, e) => dialog.IsPrimaryButtonEnabled = box.Text.Trim().Length > 0;

        bool accepted = false;
        box.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Enter && dialog.IsPrimaryButtonEnabled)
            {
                e.Handled = true;
                accepted = true;
                dialog.Hide();
            }
        };

        try
        {
            accepted |= await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            // Уже открыт другой диалог
            Shared.Diagnostics.AppLog.Write("Folders", ex);
            return null;
        }
        return accepted ? box.Text.Trim() : null;
    }

    #endregion

    #region Перетаскивание

    /// <summary>Пункт, на котором нажата кнопка мыши, и точка нажатия: кандидат на перетаскивание.</summary>
    private (NavigationViewItem Item, Point Origin)? _pressed;
    private PointerRoutedEventArgs? _lastPress;

    /// <summary>
    /// NavigationViewItem захватывает указатель при нажатии, и CanDrag сам перетаскивание не начинает.
    /// Начинаем его вручную, когда мышь с нажатой кнопкой сдвинулась на несколько пикселей.
    /// Обработчики ловят и уже обработанные события; нажатие на вложенный пункт
    /// всплывает к папке — кандидатом становится самый внутренний пункт.
    /// </summary>
    private void EnableDragStart(NavigationViewItem item)
    {
        item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) =>
        {
            if (e == _lastPress) return;
            _lastPress = e;
            var point = e.GetCurrentPoint(item);
            _pressed = e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && point.Properties.IsLeftButtonPressed
                ? (item, point.Position)
                : null;
        }), true);

        item.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(async (s, e) =>
        {
            if (_pressed is not { } pressed || pressed.Item != item) return;
            var point = e.GetCurrentPoint(item);
            if (!point.Properties.IsLeftButtonPressed)
            {
                _pressed = null;
                return;
            }
            if (Math.Abs(point.Position.X - pressed.Origin.X) < DragThreshold
                && Math.Abs(point.Position.Y - pressed.Origin.Y) < DragThreshold) return;

            _pressed = null;
            try
            {
                await item.StartDragAsync(point);
            }
            catch (Exception ex)
            {
                Shared.Diagnostics.AppLog.Write("Folders", ex);
            }
        }), true);

        item.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((s, e) => _pressed = null), true);
    }

    private const double DragThreshold = 6;

    private void StartDrag(DragStartingEventArgs e, DragItem item, string caption)
    {
        _drag = item;
        e.Data.SetText(caption);
        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.AllowedOperations = DataPackageOperation.Move;
    }

    private bool CanDropInto(PlaylistFolder target) => _drag switch
    {
        { PlaylistId: { } id } => Folders.FindPlaylistFolder(id) != target,
        { Folder: { } folder } => Folders.FindParent(folder) != target && Folders.CanMoveFolder(folder, target),
        _ => false
    };

    private static void AcceptDrop(DragEventArgs e, bool accept, string caption)
    {
        if (!accept) return;
        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = caption;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = false;
    }

    private void DropInto(DragEventArgs e, PlaylistFolder target)
    {
        if (!CanDropInto(target)) return;
        e.Handled = true;
        var drag = _drag!;
        _drag = null;

        // Панель перестраивается после завершения броска: элемент-получатель ещё в обработке
        Defer(() =>
        {
            if (drag.PlaylistId != null) Folders.MovePlaylist(drag.PlaylistId, target);
            else if (drag.Folder != null) Folders.MoveFolder(drag.Folder, target);
        });
    }

    #endregion
}
