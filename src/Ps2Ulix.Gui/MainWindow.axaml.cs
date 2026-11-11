using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ps2Ulix.Core;
using Ps2Ulix.Gui.Controls;

namespace Ps2Ulix.Gui;

/// <summary>
/// Console-style shell. Mouse: click a drive or game to open it, Back in the title bar.
/// Keyboard: arrows move, Enter opens, Esc/Backspace goes back, typing on a drive searches.
/// </summary>
public partial class MainWindow : Window, IDialogService
{
    private static readonly FilePickerFileType IsoType = new("PS2 disc image (*.iso, *.zso)") { Patterns = ["*.iso", "*.ISO", "*.zso", "*.ZSO"] };
    private static readonly TimeSpan ToastTime = TimeSpan.FromSeconds(3.5);

    /// <summary>How often to look for USB drives being plugged in or out.</summary>
    private static readonly TimeSpan DriveWatchInterval = TimeSpan.FromSeconds(2);

    /// <summary>One sheet at a time; e.g. an error raised while another is closing waits its turn.</summary>
    private readonly SemaphoreSlim _sheetGate = new(1, 1);
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _driveWatch;
    private readonly List<MenuAction> _gameActions = [];
    private MainViewModel? _boundVm;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, (_, _) => Intro.Skip(), RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        GameList.ContainerPrepared += (_, e) => Motion.Enter(e.Container, e.Index);

        _toastTimer = new DispatcherTimer { Interval = ToastTime };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastText.Opacity = 0;
        };

        _driveWatch = new DispatcherTimer { Interval = DriveWatchInterval };
        _driveWatch.Tick += (_, _) =>
        {
            if (ViewModel is { IsBusy: false } vm && !SheetLayer.IsVisible)
                _ = vm.RefreshDrivesAsync();
        };

        Intro.PropertyChanged += (_, e) =>
        {
            if (e.Property != BootIntro.TimeProperty)
                return;
            var opacity = BootIntro.TitleOpacityAt(Intro.Time);
            IntroTitle.Opacity = opacity;
            IntroSubtitle.Opacity = opacity * 0.85;
        };
        Intro.Completed += (_, _) => EndIntro();
        Opened += (_, _) =>
        {
            _driveWatch.Start();
            // Take focus straight away, so a key press can skip the boot scene without clicking first.
            Activate();
            Focus();
            if (ShowIntro)
                Intro.Play();
            else
                EndIntro();
        };
        Closed += (_, _) => _driveWatch.Stop();
    }

    /// <summary>Play the boot scene on open; tests turn it off.</summary>
    public bool ShowIntro { get; set; } = true;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_boundVm is not null)
        {
            _boundVm.PropertyChanged -= OnViewModelChanged;
            _boundVm.ToastRaised -= ShowToast;
            _boundVm.VisibleGames.CollectionChanged -= OnVisibleGamesChanged;
        }
        _boundVm = ViewModel;
        if (_boundVm is null)
            return;
        _boundVm.PropertyChanged += OnViewModelChanged;
        _boundVm.ToastRaised += ShowToast;
        _boundVm.VisibleGames.CollectionChanged += OnVisibleGamesChanged;
        BuildActions(_boundVm);
    }

    private void BuildActions(MainViewModel vm)
    {
        Geometry? Icon(string key) => this.TryFindResource(key, out var g) ? g as Geometry : null;

        _gameActions.Clear();
        _gameActions.AddRange(
        [
            new MenuAction(() => "OPL settings", Icon("IcSliders"), () => vm.SettingsCommand.Execute(null), () => vm.SettingsCommand.CanExecute(null)),
            new MenuAction(() => "Convert format", Icon("IcConvert"), () => vm.ConvertCommand.Execute(null), () => vm.ConvertCommand.CanExecute(null)),
            new MenuAction(() => "Rename", Icon("IcPencil"), () => vm.RenameCommand.Execute(null), () => vm.RenameCommand.CanExecute(null)),
            new MenuAction(() => "Export as ISO", Icon("IcJoin"), () => vm.JoinCommand.Execute(null), () => vm.JoinCommand.CanExecute(null)),
            new MenuAction(() => "Delete", Icon("IcTrash"), () => vm.DeleteCommand.Execute(null), () => vm.DeleteCommand.CanExecute(null), isDanger: true),
        ]);
        ActionList.ItemsSource = _gameActions;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var action in _gameActions)
            action.Refresh();
        if (e.PropertyName is nameof(MainViewModel.Screen) or nameof(MainViewModel.IsBusy))
            Dispatcher.UIThread.Post(FocusCurrent, DispatcherPriority.Background);
    }

    /// <summary>Games load after the drive opens; highlight the first one once they arrive.</summary>
    private void OnVisibleGamesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (ViewModel is { IsDriveScreen: true, SelectedGame: null, VisibleGames.Count: > 0 } vm)
            vm.SelectedGame = vm.VisibleGames[0];
    }

    private void FocusCurrent()
    {
        if (ViewModel is not { } vm || SheetLayer.IsVisible || IntroLayer.IsVisible || vm.IsBusy || SearchBox.IsFocused)
            return;
        var list = vm.IsGameScreen ? ActionList : vm.IsDriveScreen ? GameList : DriveList;
        if (list.SelectedIndex < 0 && list.ItemCount > 0)
            list.SelectedIndex = 0;
        if (list.ContainerFromIndex(Math.Max(0, list.SelectedIndex)) is { } item)
            item.Focus();
        else
            list.Focus();
    }

    private void EndIntro()
    {
        IntroLayer.Opacity = 0;
        IntroLayer.IsHitTestVisible = false;
        DispatcherTimer.RunOnce(() =>
        {
            IntroLayer.IsVisible = false;
            FocusCurrent();
        }, TimeSpan.FromMilliseconds(ShowIntro ? 360 : 1));
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastText.Opacity = 1;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // Keyboard ---------------------------------------------------------------------------------

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Intro.IsPlaying)
        {
            Intro.Skip();
            e.Handled = true;
            return;
        }
        if (SheetLayer.IsVisible || ViewModel is not { } vm)
            return;
        if (vm.IsBusy)
        {
            if (e.Key is Key.Escape)
            {
                vm.CancelCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }
        if (ReferenceEquals(e.Source, SearchBox))
        {
            HandleSearchKey(vm, e);
            return;
        }
        if (e.Source is TextBox)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                Confirm();
                break;
            case Key.Escape:
                vm.BackCommand.Execute(null);
                break;
            case Key.Back when vm is { IsDriveScreen: true, HasSearch: true }:
                vm.SearchText = vm.SearchText[..^1];
                break;
            case Key.Back:
                vm.BackCommand.Execute(null);
                break;
            case Key.F2 when !vm.IsBrowserScreen && vm.RenameCommand.CanExecute(null):
                vm.RenameCommand.Execute(null);
                break;
            case Key.Delete when !vm.IsBrowserScreen && vm.DeleteCommand.CanExecute(null):
                vm.DeleteCommand.Execute(null);
                break;
            case Key.F5:
                vm.RefreshDrivesCommand.Execute(null);
                break;
            case Key.O when e.KeyModifiers == KeyModifiers.Control && vm.AddGameCommand.CanExecute(null):
                vm.AddGameCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>In the search box: Enter or Down goes to the results, Esc clears the search, then leaves.</summary>
    private void HandleSearchKey(MainViewModel vm, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when vm.HasSearch:
                vm.SearchText = "";
                break;
            case Key.Escape or Key.Enter or Key.Down:
                if (GameList.SelectedIndex < 0 && GameList.ItemCount > 0)
                    GameList.SelectedIndex = 0;
                if (GameList.ContainerFromIndex(Math.Max(0, GameList.SelectedIndex)) is { } item)
                    item.Focus(NavigationMethod.Directional);
                else
                    GameList.Focus();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Typing anywhere on a drive screen goes into the search box.</summary>
    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (ViewModel is not { IsDriveScreen: true, IsBusy: false } vm || SheetLayer.IsVisible
            || e.Source is TextBox || string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl) || e.Text == " " && !vm.HasSearch)
            return;
        vm.SearchText += e.Text;
        SearchBox.Focus();
        SearchBox.CaretIndex = vm.SearchText.Length;
        e.Handled = true;
    }

    private void Confirm()
    {
        if (ViewModel is not { } vm)
            return;
        if (vm.IsGameScreen)
            (ActionList.SelectedItem as MenuAction)?.Run();
        else if (vm.IsDriveScreen)
            vm.OpenGameCommand.Execute(null);
        else
            vm.OpenDriveCommand.Execute(null);
    }

    // Mouse: one click opens, as on a console menu ---------------------------------------------

    private static bool OnItem(RoutedEventArgs e) => (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null;

    private void OnDriveTapped(object? sender, TappedEventArgs e)
    {
        if (OnItem(e))
            ViewModel?.OpenDriveCommand.Execute(null);
    }

    private void OnGameTapped(object? sender, TappedEventArgs e)
    {
        if (OnItem(e))
            ViewModel?.OpenGameCommand.Execute(null);
    }

    private void OnActionTapped(object? sender, TappedEventArgs e)
    {
        if (OnItem(e))
            (ActionList.SelectedItem as MenuAction)?.Run();
    }

    // IDialogService ---------------------------------------------------------------------------

    public async Task<string?> PickIsoAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pick a PS2 ISO",
            AllowMultiple = false,
            FileTypeFilter = [IsoType, FilePickerFileTypes.All],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveIsoAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save ISO as",
            SuggestedFileName = string.Join("_", suggestedName.Split(Path.GetInvalidFileNameChars())),
            DefaultExtension = "iso",
            FileTypeChoices = [IsoType],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick drive or folder with ul.cfg",
            AllowMultiple = false,
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public Task<PromptResult?> PromptAsync(PromptOptions options)
    {
        var sheet = new PromptSheet(options);
        return ShowSheetAsync(sheet, sheet.Result);
    }

    public Task<bool> EditGameConfigAsync(string title, OplGameConfig config)
    {
        var sheet = new SettingsSheet(title, config);
        return ShowSheetAsync(sheet, sheet.Result);
    }

    /// <summary>Shows a dialog band over the blurred, disabled shell until its result arrives.</summary>
    private async Task<T> ShowSheetAsync<T>(Control sheet, Task<T> result)
    {
        await _sheetGate.WaitAsync();
        try
        {
            SheetHost.Opacity = 0;
            SheetHost.Content = sheet;
            SheetLayer.IsVisible = true;
            Shell.IsEnabled = false;
            Shell.Effect = new BlurEffect { Radius = 10 };
            Dispatcher.UIThread.Post(() => SheetHost.Opacity = 1, DispatcherPriority.Render);
            return await result;
        }
        finally
        {
            SheetHost.Content = null;
            SheetLayer.IsVisible = false;
            Shell.IsEnabled = true;
            Shell.Effect = null;
            _sheetGate.Release();
            Dispatcher.UIThread.Post(FocusCurrent, DispatcherPriority.Background);
        }
    }

    // Drag and drop ----------------------------------------------------------------------------

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.Data.Contains(DataFormats.Files);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Opacity = hasFiles ? 1 : 0;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => DropOverlay.Opacity = 0;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.Opacity = 0;
        var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        if (ViewModel is null)
            return;
        // One at a time; each add waits for the previous to finish.
        foreach (var path in paths)
            await ViewModel.AddIsoAsync(path);
    }
}
