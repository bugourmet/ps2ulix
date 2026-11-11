using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ps2Ulix.Core;
using Ps2Ulix.Gui.Controls;

namespace Ps2Ulix.Gui;

/// <summary>Console-style screens: drive browser, a drive's games, one game.</summary>
public enum AppScreen
{
    Browser,
    Drive,
    Game,
}

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly Color DefaultTint = Color.FromRgb(0x4C, 0x8D, 0xFF);

    /// <summary>Storage formats, in the order offered by Add and Convert (index 0 = UL).</summary>
    private static readonly string[] Formats =
        ["UL: split into 1 GiB parts (any size on FAT32)", "ISO: one file in CD/ or DVD/", "ZSO: compressed file in CD/ or DVD/"];

    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;
    private int _reloadGeneration;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _ = RefreshDrivesAsync();
    }

    public ObservableCollection<DriveItem> Drives { get; } = [];

    public ObservableCollection<GameRow> Games { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddGameCommand), nameof(CheckCommand), nameof(RebuildCommand), nameof(DownloadCoversCommand))]
    private DriveItem? _selectedDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand), nameof(RenameCommand), nameof(DeleteCommand), nameof(ConvertCommand),
        nameof(SettingsCommand))]
    private GameRow? _selectedGame;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddGameCommand), nameof(JoinCommand), nameof(RenameCommand), nameof(DeleteCommand),
        nameof(CheckCommand), nameof(RebuildCommand), nameof(BrowseCommand), nameof(RefreshDrivesCommand),
        nameof(ConvertCommand), nameof(SettingsCommand), nameof(DownloadCoversCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _status = "Pick a USB drive or folder.";

    [ObservableProperty]
    private string _driveSummary = "";

    /// <summary>Second job line: bytes done, speed, time left.</summary>
    [ObservableProperty]
    private string _jobDetail = "";

    [ObservableProperty]
    private string _searchText = "";

    /// <summary>"All", "UL", "ISO" or "ZSO".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAll), nameof(IsFilterUl), nameof(IsFilterIso), nameof(IsFilterZso))]
    private string _formatFilter = "All";

    /// <summary>Index into <see cref="SortModes"/>.</summary>
    [ObservableProperty]
    private int _sortIndex;

    [ObservableProperty]
    private Color _ambientTint = DefaultTint;

    [ObservableProperty]
    private long _capacityTotal;

    [ObservableProperty]
    private IReadOnlyList<CapacitySegment> _capacitySegments = [];

    [ObservableProperty]
    private string _capacityText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIssues), nameof(IsHealthy))]
    private int _issueCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowserScreen), nameof(IsDriveScreen), nameof(IsGameScreen), nameof(ScreenTitle), nameof(KeyHints))]
    private AppScreen _screen = AppScreen.Browser;

    /// <summary>Short message to show briefly, e.g. after a job ends.</summary>
    public event Action<string>? ToastRaised;

    public bool IsBrowserScreen => Screen == AppScreen.Browser;

    public bool IsDriveScreen => Screen == AppScreen.Drive;

    public bool IsGameScreen => Screen == AppScreen.Game;

    public string ScreenTitle => Screen == AppScreen.Browser ? "Browser" : SelectedDrive?.Title ?? "";

    /// <summary>Big title on the drive screen: the highlighted game, else the drive.</summary>
    public string HeaderTitle => SelectedGame?.Name ?? SelectedDrive?.Title ?? "";

    public string HeaderDetail => SelectedGame is { } g
        ? $"{g.DisplayId}   {g.Media}   {g.Format}"
        : LibraryCount;

    public string FreeText => SelectedDrive is { Total: > 0 } d ? $"{SizeFormat.Format(d.Free)} free of {SizeFormat.Format(d.Total)}" : "";

    public bool HasSearch => SearchText.Length > 0;

    public string SortLabel => SortModes[SortIndex];

    /// <summary>Footer line: what the keyboard does on this screen.</summary>
    public string KeyHints => Screen switch
    {
        AppScreen.Browser => "Click a drive or press Enter to open   ·   F5 refresh",
        AppScreen.Drive => "Click a game to open   ·   Type to search   ·   Esc back   ·   Drop .iso / .zso to add",
        _ => "Click an action or press Enter   ·   F2 rename   ·   Del delete   ·   Esc back",
    };

    /// <summary>Opens the highlighted drive.</summary>
    [RelayCommand]
    private void OpenDrive()
    {
        if (SelectedDrive is null || IsBusy)
            return;
        Screen = AppScreen.Drive;
    }

    /// <summary>Opens the highlighted game's screen.</summary>
    [RelayCommand]
    private void OpenGame()
    {
        if (SelectedGame is null || IsBusy)
            return;
        Screen = AppScreen.Game;
    }

    /// <summary>One step back: search first, then screen.</summary>
    [RelayCommand]
    private void Back()
    {
        if (Screen == AppScreen.Game)
            Screen = AppScreen.Drive;
        else if (Screen == AppScreen.Drive && SearchText.Length > 0)
            SearchText = "";
        else if (Screen == AppScreen.Drive)
            Screen = AppScreen.Browser;
    }

    [RelayCommand]
    private void GoToBrowser() => Screen = AppScreen.Browser;

    [RelayCommand]
    private void GoToDrive()
    {
        if (SelectedDrive is not null)
            Screen = AppScreen.Drive;
    }

    [RelayCommand]
    private void CycleSort() => SortIndex = (SortIndex + 1) % SortModes.Count;

    [RelayCommand]
    private void CycleFilter() => FormatFilter = FormatFilter switch { "All" => "UL", "UL" => "ISO", "ISO" => "ZSO", _ => "All" };

    public static IReadOnlyList<string> SortModes { get; } = ["Name", "Size", "Format", "Game ID"];

    public ObservableCollection<GameRow> VisibleGames { get; } = [];

    public ObservableCollection<DriveIssue> Issues { get; } = [];

    public bool IsFilterAll => FormatFilter == "All";

    public bool IsFilterUl => FormatFilter == "UL";

    public bool IsFilterIso => FormatFilter == "ISO";

    public bool IsFilterZso => FormatFilter == "ZSO";

    public bool HasIssues => IssueCount > 0;

    public bool IsHealthy => SelectedDrive is not null && !HasIssues;

    public bool HasNoVisibleGames => VisibleGames.Count == 0;

    public string LibraryCount => VisibleGames.Count == Games.Count
        ? $"{Games.Count} {(Games.Count == 1 ? "game" : "games")}"
        : $"{VisibleGames.Count} of {Games.Count} games";

    public string EmptyText => SelectedDrive is null
        ? "Pick a drive on the left"
        : Games.Count == 0 ? "Drop a PS2 .iso anywhere to add it" : "Nothing matches that filter";

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearch));
        UpdateVisibleGames();
    }

    partial void OnFormatFilterChanged(string value) => UpdateVisibleGames();

    partial void OnSortIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SortLabel));
        UpdateVisibleGames();
    }

    partial void OnSelectedGameChanged(GameRow? value)
    {
        AmbientTint = value?.CoverColor ?? DefaultTint;
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderDetail));
    }

    [RelayCommand]
    private void SetFilter(string filter) => FormatFilter = filter;

    [RelayCommand]
    private void ClearSelection() => SelectedGame = null;

    /// <summary>Applies search, format filter and sort with minimal list edits, so cards glide instead of rebuilding.</summary>
    private void UpdateVisibleGames()
    {
        IEnumerable<GameRow> query = Games;
        var search = SearchText.Trim();
        if (search.Length > 0)
        {
            query = query.Where(g => g.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (g.GameId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        query = FormatFilter switch
        {
            "UL" => query.Where(g => g.IsUl),
            "ISO" => query.Where(g => g.IsIso),
            "ZSO" => query.Where(g => g.IsZso),
            _ => query,
        };
        var desired = (SortIndex switch
        {
            1 => query.OrderByDescending(g => g.SizeBytes),
            2 => query.OrderBy(g => g.IsUl ? 0 : g.IsIso ? 1 : 2).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            3 => query.OrderBy(g => g.GameId, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
        }).ToList();

        for (var i = VisibleGames.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(VisibleGames[i]))
                VisibleGames.RemoveAt(i);
        }
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < VisibleGames.Count && ReferenceEquals(VisibleGames[i], desired[i]))
                continue;
            var at = VisibleGames.IndexOf(desired[i]);
            if (at >= 0)
                VisibleGames.Move(at, i);
            else
                VisibleGames.Insert(i, desired[i]);
        }
        OnPropertyChanged(nameof(LibraryCount));
        OnPropertyChanged(nameof(HasNoVisibleGames));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(HeaderDetail));
    }

    private void UpdateCapacity(IReadOnlyCollection<GameRow> rows)
    {
        var drive = SelectedDrive;
        if (drive is null || drive.Total <= 0)
        {
            CapacityTotal = 0;
            CapacitySegments = [];
            CapacityText = "";
            return;
        }
        var segments = rows
            .OrderBy(g => g.IsUl ? 0 : g.IsIso ? 1 : 2)
            .Select(g => new CapacitySegment(g.IsUl ? SegmentKind.Ul : g.IsIso ? SegmentKind.Iso : SegmentKind.Zso, g.SizeBytes, g))
            .ToList();
        var games = rows.Sum(g => g.SizeBytes);
        var other = drive.Total - drive.Free - games;
        if (other > 0)
            segments.Add(new CapacitySegment(SegmentKind.Other, other));
        CapacityTotal = drive.Total;
        CapacitySegments = segments;
        CapacityText = $"{SizeFormat.Format(games)} games · {SizeFormat.Format(Math.Max(0, other))} other · {SizeFormat.Format(drive.Free)} free of {SizeFormat.Format(drive.Total)}";
    }

    /// <summary>Decodes covers one by one off the UI thread; cards fill in as they arrive.</summary>
    private async Task LoadCoversAsync(IReadOnlyList<GameRow> rows, string root, int generation)
    {
        foreach (var row in rows)
        {
            var (cover, color) = await Task.Run(() => row.ReadCover(root));
            if (generation != _reloadGeneration)
                return;
            row.SetCover(cover, color);
            if (ReferenceEquals(row, SelectedGame))
                AmbientTint = color ?? DefaultTint;
        }
    }

    private bool HasDrive => SelectedDrive is not null && !IsBusy;

    private bool HasGame => SelectedGame is not null && HasDrive;

    private bool HasUlGame => SelectedGame?.Ul is not null && HasDrive;

    private bool HasGameId => SelectedGame?.GameId is not null && HasDrive;

    private UlDrive Drive => new(SelectedDrive!.Path);

    private IsoFolders Folders => new(SelectedDrive!.Path);

    partial void OnSelectedDriveChanged(DriveItem? value)
    {
        Status = value is null ? "Pick a USB drive or folder." : "Drop a PS2 .iso on the window to add it.";
        OnPropertyChanged(nameof(IsHealthy));
        OnPropertyChanged(nameof(ScreenTitle));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(FreeText));
        SearchText = "";
        _ = ReloadAsync();
    }

    /// <summary>Drives that answer slower than this are skipped (a sleeping disk, a stuck card reader).</summary>
    private static readonly TimeSpan DriveScanTimeout = TimeSpan.FromSeconds(3);

    private bool _scanning;
    private bool _scannedOnce;

    /// <summary>
    /// Rescans drives off the UI thread and merges the result, so selection survives and a slow drive
    /// can't freeze the window. Also called every few seconds to notice USB drives being plugged in or out.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBrowse))]
    public async Task RefreshDrivesAsync()
    {
        if (_scanning)
            return;
        _scanning = true;
        try
        {
            var scanned = await Task.Run(ScanDrives);
            var firstScan = !_scannedOnce;
            _scannedOnce = true;

            // Folders opened by hand stay listed; real drives follow the scan.
            var folders = Drives.Where(d => d.Kind == DeviceKind.Folder && scanned.All(s => s.Path != d.Path)).ToList();
            var wanted = scanned.Concat(folders).ToList();
            var selected = SelectedDrive;

            for (var i = Drives.Count - 1; i >= 0; i--)
            {
                if (wanted.All(w => w.Path != Drives[i].Path))
                    Drives.RemoveAt(i);
            }
            foreach (var drive in wanted)
            {
                var at = Drives.ToList().FindIndex(d => d.Path == drive.Path);
                if (at < 0)
                {
                    Drives.Add(drive);
                    if (!firstScan && drive.IsRemovable)
                        ToastRaised?.Invoke($"Drive connected: {drive.Title}");
                }
                else if (!ReferenceEquals(Drives[at], selected) && Drives[at] != drive)
                {
                    Drives[at] = drive; // fresher free space; the selected one is left alone
                }
            }

            if (selected is not null && Drives.All(d => d.Path != selected.Path))
            {
                ToastRaised?.Invoke($"Drive removed: {selected.Title}");
                Screen = AppScreen.Browser;
                SelectedDrive = null;
            }
            SelectedDrive ??= Drives.FirstOrDefault(d => d.IsRemovable) ?? (firstScan ? null : Drives.FirstOrDefault());
        }
        finally
        {
            _scanning = false;
        }
    }

    private static List<DriveItem> ScanDrives()
    {
        DriveInfo[] all;
        try
        {
            all = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        var probes = all.Select(d => Task.Run(() =>
        {
            try
            {
                return d.IsReady ? DriveItem.FromInfo(d) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        })).ToList();
        Task.WaitAll([.. probes], DriveScanTimeout);
        return probes.Where(t => t.IsCompletedSuccessfully && t.Result is not null).Select(t => t.Result!)
            .OrderBy(d => d.IsRemovable ? 0 : 1).ThenBy(d => d.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseAsync()
    {
        var folder = await _dialogs.PickFolderAsync();
        if (folder is not null)
            OpenFolder(folder);
    }

    /// <summary>Selects a folder as the drive, adding it to the picker if needed.</summary>
    public void OpenFolder(string folder)
    {
        var item = Drives.FirstOrDefault(d => string.Equals(d.Path, folder, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            item = DriveItem.FromFolder(folder);
            Drives.Add(item);
        }
        SelectedDrive = item;
    }

    private bool CanBrowse() => !IsBusy;

    /// <summary>Reloads the game table (UL games, then CD/DVD folder images); health summary is from <see cref="UlDrive.Check"/>.</summary>
    public async Task ReloadAsync()
    {
        var generation = ++_reloadGeneration;
        var selectedKey = SelectedGame?.Key;
        var selectedId = (SelectedGame?.GameId, SelectedGame?.IsUl, SelectedGame?.IsZso);
        Games.Clear();
        UpdateVisibleGames();
        Issues.Clear();
        IssueCount = 0;
        UpdateCapacity([]);
        DriveSummary = "";
        if (SelectedDrive is null)
            return;
        try
        {
            var drive = Drive;
            var folders = Folders;
            var (report, rows) = await Task.Run(() =>
            {
                var r = drive.Check();
                var all = r.Games.Select(g => new GameRow(drive, g))
                    .Concat(folders.LoadGames().Select(g => new GameRow(g)))
                    .ToList();
                return (r, all);
            });
            if (generation != _reloadGeneration)
                return; // a newer reload owns the table
            Games.Clear();
            foreach (var row in rows)
                Games.Add(row);
            UpdateVisibleGames();
            UpdateCapacity(rows);
            if (selectedKey is not null)
            {
                // Same game, or after a rename the one game with the same ID and format.
                SelectedGame = VisibleGames.FirstOrDefault(g => g.Key == selectedKey)
                    ?? (VisibleGames.Where(g => (g.GameId, g.IsUl, g.IsZso) == selectedId).ToList() is [var only] ? only : null);
            }
            if (Screen == AppScreen.Game && SelectedGame is null)
                Screen = AppScreen.Drive;
            foreach (var issue in report.Issues.OrderByDescending(i => i.Severity))
                Issues.Add(issue);
            IssueCount = report.Issues.Count;
            var errors = report.Issues.Count(i => i.Severity == IssueSeverity.Error);
            var warnings = report.Issues.Count - errors;
            DriveSummary = $"{rows.Count} game(s), {SizeFormat.Format(rows.Sum(r => r.SizeBytes))}"
                + (report.Issues.Count == 0 ? "" : $" · {errors} error(s), {warnings} warning(s), run Check");
            await LoadCoversAsync(rows, drive.Root, generation);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (generation == _reloadGeneration)
                DriveSummary = $"Can't read drive: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasDrive))]
    private async Task AddGameAsync()
    {
        var iso = await _dialogs.PickIsoAsync();
        if (iso is not null)
            await AddIsoAsync(iso);
    }

    /// <summary>Add flow for a picked or dropped ISO/ZSO: read info, confirm name/media/format, write with progress.</summary>
    public async Task AddIsoAsync(string isoPath)
    {
        if (!HasDrive)
            return;
        Ps2IsoInfo info;
        try
        {
            info = await Task.Run(() => Ps2Iso.Read(isoPath));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            await ShowErrorAsync($"Can't use {Path.GetFileName(isoPath)}", ex.Message);
            return;
        }

        var answer = await _dialogs.PromptAsync(new PromptOptions(
            "Add game",
            $"{Path.GetFileName(isoPath)}\n{info.GameId} · {SizeFormat.Format(info.Size)}\n\nGame name (max 32 chars, ASCII):",
            Input: UlDrive.NameFromFile(isoPath),
            Media: info.Media,
            OkText: "Add",
            Choices: Formats));
        if (answer is null)
            return;

        var drive = Drive;
        var folders = Folders;
        await RunAsync($"Adding {answer.Input}…", info.Size, (progress, ct) =>
        {
            if (answer.Choice == 0)
                drive.AddGame(isoPath, answer.Input, answer.Media, progress, ct);
            else
                folders.AddGame(isoPath, answer.Input, answer.Media, answer.Choice == 1 ? IsoFormat.Iso : IsoFormat.Zso, progress, ct);
        }, $"Added {answer.Input}.");
    }

    [RelayCommand(CanExecute = nameof(HasUlGame))]
    private async Task JoinAsync()
    {
        var game = SelectedGame!;
        var output = await _dialogs.PickSaveIsoAsync($"{game.GameId}.{game.Name}.iso");
        if (output is null)
            return;
        var drive = Drive;
        await RunAsync($"Joining {game.Name}…", game.SizeBytes, (progress, ct) =>
        {
            // The save picker already asked about overwriting.
            if (File.Exists(output))
                File.Delete(output);
            drive.JoinGame(game.Ul!, output, progress, ct);
        }, $"Wrote {output}.", reload: false);
    }

    [RelayCommand(CanExecute = nameof(HasGame))]
    private async Task RenameAsync()
    {
        var game = SelectedGame!;
        var answer = await _dialogs.PromptAsync(new PromptOptions(
            "Rename game", "New name (max 32 chars, ASCII):", Input: game.Name, OkText: "Rename"));
        if (answer is null || answer.Input == game.Name)
            return;
        var drive = Drive;
        var folders = Folders;
        await RunAsync("Renaming…", null, (_, _) =>
        {
            if (game.Ul is not null)
                drive.RenameGame(game.Ul, answer.Input);
            else
                folders.RenameGame(game.Iso!, answer.Input);
        }, $"Renamed to {answer.Input}.");
    }

    [RelayCommand(CanExecute = nameof(HasGame))]
    private async Task DeleteAsync()
    {
        var game = SelectedGame!;
        var answer = await _dialogs.PromptAsync(new PromptOptions(
            "Delete game", $"Delete '{game.Name}' ({game.GameId}, {game.Format}) from the drive?\nThis can't be undone.",
            OkText: "Delete"));
        if (answer is null)
            return;
        var drive = Drive;
        var folders = Folders;
        await RunAsync("Deleting…", null, (_, _) =>
        {
            if (game.Ul is not null)
                drive.DeleteGame(game.Ul);
            else
                folders.DeleteGame(game.Iso!);
        }, $"Deleted {game.Name}.");
    }

    /// <summary>Converts between UL, ISO and ZSO; the original is removed only after the new copy is written.</summary>
    [RelayCommand(CanExecute = nameof(HasGame))]
    private async Task ConvertAsync()
    {
        var game = SelectedGame!;
        var current = game.Ul is not null ? 0 : game.Iso!.Format == IsoFormat.Iso ? 1 : 2;
        var targets = Enumerable.Range(0, Formats.Length).Where(i => i != current).ToList();
        var answer = await _dialogs.PromptAsync(new PromptOptions(
            "Convert game", $"Convert '{game.Name}' ({game.Format}) to:", OkText: "Convert",
            Choices: targets.Select(i => Formats[i]).ToList(), ChoiceLabel: "Target:"));
        if (answer is null)
            return;
        var target = targets[answer.Choice];

        var drive = Drive;
        var folders = Folders;
        long total;
        try
        {
            // Progress counts ISO bytes, and a ZSO's file size isn't its ISO size.
            total = game.IsZso ? await Task.Run(() => Ps2Iso.Read(game.Iso!.Path).Size) : game.SizeBytes;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            await ShowErrorAsync("Convert game", ex.Message);
            return;
        }
        await RunAsync($"Converting {game.Name}…", total, (progress, ct) =>
        {
            if (target == 0)
            {
                drive.AddGame(game.Iso!.Path, UlDrive.NameFromFile(game.Name + ".iso"), game.MediaValue, progress, ct);
                folders.DeleteGame(game.Iso);
                return;
            }
            Func<Stream> open = game.Ul is not null ? () => drive.OpenGameImage(game.Ul) : () => Ps2Iso.OpenImage(game.Iso!.Path);
            Ps2IsoInfo info;
            using (var image = open())
                info = Ps2Iso.Read(image);
            folders.AddGame(open, info, game.Name, game.MediaValue, target == 1 ? IsoFormat.Iso : IsoFormat.Zso, progress, ct);
            if (game.Ul is not null)
                drive.DeleteGame(game.Ul);
            else
                folders.DeleteGame(game.Iso!);
        }, $"Converted {game.Name}.");
    }

    [RelayCommand(CanExecute = nameof(HasGameId))]
    private async Task SettingsAsync()
    {
        var game = SelectedGame!;
        var root = SelectedDrive!.Path;
        OplGameConfig config;
        try
        {
            config = OplGameConfig.Load(root, game.GameId!);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            await ShowErrorAsync("Game settings", ex.Message);
            return;
        }
        if (!await _dialogs.EditGameConfigAsync($"OPL settings · {game.Name} ({game.GameId})", config))
            return;
        await RunAsync("Saving settings…", null, (_, _) => config.Save(root, game.GameId!), "Settings saved.", reload: false);
    }

    /// <summary>Downloads front covers into ART/ for every game with a known ID; existing art is kept.</summary>
    [RelayCommand(CanExecute = nameof(HasDrive))]
    private async Task DownloadCoversAsync()
    {
        var ids = Games.Select(g => g.GameId).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0)
        {
            Status = "No games with a known ID.";
            return;
        }
        var root = SelectedDrive!.Path;
        var downloader = new ArtDownloader(Http);
        int got = 0, had = 0, missing = 0;
        await RunAsync($"Downloading covers for {ids.Count} game(s)…", ids.Count, (progress, ct) =>
        {
            for (var i = 0; i < ids.Count; i++)
            {
                var result = downloader.DownloadAsync(root, ids[i], ["COV"], cancellationToken: ct).GetAwaiter().GetResult().Single();
                switch (result.Outcome)
                {
                    case ArtOutcome.Downloaded: got++; break;
                    case ArtOutcome.AlreadyExists: had++; break;
                    default: missing++; break;
                }
                progress.Report(i + 1);
            }
        }, "Covers done.", reload: false, totalIsBytes: false);
        if (got + had + missing > 0)
        {
            Status = $"Covers: {got} downloaded, {had} already there, {missing} not in the art database.";
            ToastRaised?.Invoke(Status);
        }
        if (got > 0)
            await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasDrive))]
    private async Task CheckAsync()
    {
        var drive = Drive;
        CheckReport? report = null;
        await RunAsync("Checking…", null, (_, _) => report = drive.Check(), "Check done.", reload: false);
        if (report is null)
            return;
        var details = report.Issues.Count == 0
            ? "No problems found."
            : string.Join("\n", report.Issues.Select(i => $"{(i.Severity == IssueSeverity.Error ? "ERROR" : "warn ")}  {i.Message}"));
        await _dialogs.PromptAsync(new PromptOptions("Check", $"{report.Games.Count} UL game(s) checked.", Details: details, ShowCancel: false));
    }

    [RelayCommand(CanExecute = nameof(HasDrive))]
    private async Task RebuildAsync()
    {
        var drive = Drive;
        RebuildResult? plan = null;
        await RunAsync("Scanning…", null, (_, _) => plan = drive.Rebuild(dryRun: true), "", reload: false);
        if (plan is null)
            return;
        var lines = plan.Recovered.Select(g => $"recover  {g.Name} ({g.Media}, {g.Parts} part(s))")
            .Concat(plan.Dropped.Select(g => $"drop     '{g.Name}' ({g.Startup}): no part files"))
            .Concat(plan.Incomplete.Select(m => $"skip     {m}"))
            .ToList();
        if (lines.Count == 0)
        {
            Status = "Rebuild: ul.cfg already matches the files.";
            return;
        }
        var answer = await _dialogs.PromptAsync(new PromptOptions(
            "Rebuild ul.cfg",
            $"ul.cfg will list {plan.Games.Count} game(s). Recovered games are named after their game ID; rename them afterwards. Old ul.cfg is kept as {UlDrive.BackupFileName}.",
            Details: string.Join("\n", lines),
            OkText: "Rebuild"));
        if (answer is null)
            return;
        await RunAsync("Rebuilding…", null, (_, _) => drive.Rebuild(), "ul.cfg rebuilt.");
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Runs a core operation off the UI thread with busy state, progress and error dialogs.</summary>
    /// <param name="total">Progress bar maximum in the units <c>work</c> reports; null shows an indeterminate bar.</param>
    /// <param name="totalIsBytes">Show size, speed and time left; off for counts like covers.</param>
    private async Task RunAsync(string busyText, long? total, Action<IProgress<long>, CancellationToken> work,
        string doneText, bool reload = true, bool totalIsBytes = true)
    {
        IsBusy = true;
        Status = busyText;
        Progress = 0;
        JobDetail = "";
        IsProgressIndeterminate = total is null;
        _cts = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var progress = new Progress<long>(done =>
        {
            Progress = total is > 0 ? done * 100.0 / total.Value : 0;
            JobDetail = total is > 0 ? DescribeProgress(done, total.Value, clock.Elapsed, totalIsBytes) : "";
        });
        try
        {
            await Task.Run(() => work(progress, _cts.Token));
            Status = doneText;
            if (doneText.Length > 0)
                ToastRaised?.Invoke(doneText);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
            ToastRaised?.Invoke(Status);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Status = "Failed.";
            await ShowErrorAsync(busyText.TrimEnd('…'), ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            Progress = 0;
            _cts.Dispose();
            _cts = null;
        }
        if (reload)
            await ReloadAsync();
    }

    private static string DescribeProgress(long done, long total, TimeSpan elapsed, bool isBytes)
    {
        if (!isBytes)
            return $"{done} of {total}";
        var text = $"{SizeFormat.Format(done)} of {SizeFormat.Format(total)}";
        if (elapsed.TotalSeconds < 1 || done <= 0)
            return text;
        var rate = done / elapsed.TotalSeconds;
        var left = TimeSpan.FromSeconds((total - done) / rate);
        var eta = left.TotalMinutes >= 1 ? $"{(int)left.TotalMinutes}m {left.Seconds:00}s" : $"{left.Seconds}s";
        return $"{text} · {SizeFormat.Format((long)rate)}/s · {eta} left";
    }

    private Task ShowErrorAsync(string title, string message) =>
        _dialogs.PromptAsync(new PromptOptions(title, message, ShowCancel: false));

    private static bool IsExpected(Exception ex) => ex is IOException or InvalidDataException
        or InvalidOperationException or ArgumentException or UnauthorizedAccessException or HttpRequestException;

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ps2ulix");
        return http;
    }
}
