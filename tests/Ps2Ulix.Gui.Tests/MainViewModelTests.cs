using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Ps2Ulix.Core;
using SkiaSharp;

namespace Ps2Ulix.Gui.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ps2ulix-gui-").FullName;
    private readonly FakeDialogs _dialogs = new();

    private static readonly UlGameEntry Okami = new("Okami", "SLES_544.39", 2, UlMedia.Dvd);
    private static readonly UlGameEntry Ico = new("Ico", "SCUS_971.13", 1, UlMedia.Cd);

    public MainViewModelTests()
    {
        var drive = new UlDrive(_dir);
        foreach (var game in new[] { Okami, Ico })
        {
            for (var p = 0; p < game.Parts; p++)
                File.WriteAllBytes(drive.GetPartPath(game, p), new byte[4096]);
        }
        UlCfg.Save(drive.CfgPath, [Okami, Ico]);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainViewModel> OpenAsync()
    {
        var vm = new MainViewModel(_dialogs);
        vm.OpenFolder(_dir);
        await vm.ReloadAsync();
        return vm;
    }

    [AvaloniaFact]
    public async Task OpenFolder_ListsGames()
    {
        var vm = await OpenAsync();

        Assert.Equal(["Okami", "Ico"], vm.Games.Select(g => g.Name));
        Assert.Equal(["DVD", "CD"], vm.Games.Select(g => g.Media));
        Assert.All(vm.Games, g => Assert.Equal("OK", g.Status));
        Assert.StartsWith("2 game(s)", vm.DriveSummary);
    }

    [AvaloniaFact]
    public async Task MissingPart_ShownInStatus()
    {
        File.Delete(new UlDrive(_dir).GetPartPath(Okami, 1));

        var vm = await OpenAsync();

        Assert.Equal("1 part(s) missing", vm.Games[0].Status);
        Assert.Contains("error", vm.DriveSummary);
    }

    [AvaloniaFact]
    public async Task Rename_UsesDialogInput()
    {
        var vm = await OpenAsync();
        vm.SelectedGame = vm.Games[0];
        _dialogs.Answers.Enqueue(new PromptResult("Okami HD", null));

        await vm.RenameCommand.ExecuteAsync(null);

        Assert.Equal(["Okami HD", "Ico"], vm.Games.Select(g => g.Name));
        Assert.Equal("Okami", _dialogs.Prompts.Single().Input);
    }

    [AvaloniaFact]
    public async Task Rename_InvalidName_ShowsError()
    {
        var vm = await OpenAsync();
        vm.SelectedGame = vm.Games[0];
        _dialogs.Answers.Enqueue(new PromptResult("Ōkami", null));

        await vm.RenameCommand.ExecuteAsync(null);

        Assert.Equal("Failed.", vm.Status);
        Assert.Equal(2, _dialogs.Prompts.Count); // rename prompt + error
        Assert.Equal(["Okami", "Ico"], vm.Games.Select(g => g.Name));
    }

    [AvaloniaFact]
    public async Task Delete_Cancelled_KeepsGame()
    {
        var vm = await OpenAsync();
        vm.SelectedGame = vm.Games[1];
        _dialogs.Answers.Enqueue(null);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Games.Count);
    }

    [AvaloniaFact]
    public async Task Delete_Confirmed_RemovesGame()
    {
        var vm = await OpenAsync();
        vm.SelectedGame = vm.Games[1];
        _dialogs.Answers.Enqueue(new PromptResult("", null));

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(["Okami"], vm.Games.Select(g => g.Name));
        Assert.False(File.Exists(new UlDrive(_dir).GetPartPath(Ico, 0)));
    }

    [AvaloniaFact]
    public async Task Rebuild_RecoversOrphans()
    {
        File.Delete(new UlDrive(_dir).CfgPath);
        var vm = await OpenAsync();
        Assert.Empty(vm.Games);
        _dialogs.Answers.Enqueue(new PromptResult("", null));

        await vm.RebuildCommand.ExecuteAsync(null);

        Assert.Equal(["SCUS_971.13", "SLES_544.39"], vm.Games.Select(g => g.Name));
        Assert.Contains("recover", _dialogs.Prompts.Single().Details);
    }

    private string MakeIso(string gameId = "SLUS_209.46")
    {
        using var iso = new Ps2Ulix.Core.Tests.TestIsoBuilder()
            .AddFile("SYSTEM.CNF;1", $"BOOT2 = cdrom0:\\{gameId};1\r\n")
            .AddFile("DATA.BIN;1", new byte[40 * 2048])
            .Build();
        // Inside the test drive (removed on Dispose); not in CD/ or DVD/, so it isn't listed as a game.
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, "src")).FullName, $"{Guid.NewGuid():N}.iso");
        File.WriteAllBytes(path, iso.ToArray());
        return path;
    }

    [AvaloniaFact]
    public async Task IsoFolderGames_ListedWithFormat()
    {
        new IsoFolders(_dir).AddGame(MakeIso(), name: "GTA", format: IsoFormat.Zso);

        var vm = await OpenAsync();

        var row = vm.Games.Single(g => g.Name == "GTA");
        Assert.Equal(("ZSO", "SLUS_209.46", "CD"), (row.Format, row.GameId, row.Media));
        Assert.Equal("UL (2)", vm.Games[0].Format);
    }

    [AvaloniaFact]
    public async Task AddIso_ChosenFormatZso_WritesToCdFolder()
    {
        var vm = await OpenAsync();
        _dialogs.Answers.Enqueue(new PromptResult("GTA", UlMedia.Cd, Choice: 2));

        await vm.AddIsoAsync(MakeIso());

        Assert.True(File.Exists(Path.Combine(_dir, "CD", "GTA.zso")));
        Assert.Equal("ZSO", vm.Games.Single(g => g.Name == "GTA").Format);
    }

    [AvaloniaFact]
    public async Task Convert_UlToIso_ReplacesGame()
    {
        new UlDrive(_dir).AddGame(MakeIso(), name: "GTA");
        var vm = await OpenAsync();
        vm.SelectedGame = vm.Games.Single(g => g.Name == "GTA");
        _dialogs.Answers.Enqueue(new PromptResult("", null, Choice: 0)); // targets offered: ISO, ZSO

        await vm.ConvertCommand.ExecuteAsync(null);

        Assert.Equal("ISO", vm.Games.Single(g => g.Name == "GTA").Format);
        Assert.DoesNotContain(new UlDrive(_dir).LoadGames(), g => g.Name == "GTA");
    }

    [AvaloniaFact]
    public async Task Settings_SavesEditedConfig()
    {
        var vm = await OpenAsync();
        vm.SelectedGame = vm.Games[0];
        _dialogs.EditConfig = c => c.Compatibility = CompatModes.AccurateReads | CompatModes.SkipVideos;

        await vm.SettingsCommand.ExecuteAsync(null);

        Assert.Equal("$Compatibility=9\r\n", File.ReadAllText(OplGameConfig.GetPath(_dir, "SLES_544.39")));
    }

    [AvaloniaFact]
    public async Task Settings_Cancelled_WritesNothing()
    {
        var vm = await OpenAsync();
        vm.SelectedGame = vm.Games[0];

        await vm.SettingsCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(Path.Combine(_dir, OplGameConfig.Folder)));
    }

    [AvaloniaFact]
    public async Task Search_And_FormatFilter_NarrowVisibleGames()
    {
        new IsoFolders(_dir).AddGame(MakeIso(), name: "GTA", format: IsoFormat.Zso);
        var vm = await OpenAsync();
        Assert.Equal(["GTA", "Ico", "Okami"], vm.VisibleGames.Select(g => g.Name));

        vm.SearchText = "scus";
        Assert.Equal(["Ico"], vm.VisibleGames.Select(g => g.Name));
        Assert.Equal("1 of 3 games", vm.LibraryCount);

        vm.SearchText = "";
        vm.SetFilterCommand.Execute("ZSO");
        Assert.Equal(["GTA"], vm.VisibleGames.Select(g => g.Name));

        vm.SetFilterCommand.Execute("All");
        vm.SortIndex = 1; // size, biggest first
        Assert.Equal(vm.Games.Max(g => g.SizeBytes), vm.VisibleGames[0].SizeBytes);
    }

    [AvaloniaFact]
    public async Task Reload_KeepsSelection()
    {
        var vm = await OpenAsync();
        vm.SelectedGame = vm.Games[1];

        await vm.ReloadAsync();

        Assert.Equal("Ico", vm.SelectedGame?.Name);
    }

    [AvaloniaFact]
    public async Task Cover_FromArtFolder_TintsBackdropWhenSelected()
    {
        var path = ArtDownloader.GetPath(_dir, Ico.Startup, "COV");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var bitmap = new SKBitmap(32, 44))
        {
            bitmap.Erase(new SKColor(220, 40, 40));
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
        }
        var vm = await OpenAsync();
        var ico = vm.Games.Single(g => g.Name == "Ico");
        Assert.True(ico.HasCover);

        vm.SelectedGame = ico;

        Assert.True(vm.AmbientTint.R > vm.AmbientTint.B);
    }

    [AvaloniaFact]
    public async Task BrokenCover_CountsAsNone()
    {
        var path = ArtDownloader.GetPath(_dir, Ico.Startup, "COV");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a png");

        var vm = await OpenAsync();

        Assert.False(vm.Games.Single(g => g.Name == "Ico").HasCover);
    }

    [AvaloniaFact]
    public async Task Sheets_ReturnResults_AndReenableShell()
    {
        var window = new MainWindow { ShowIntro = false };
        window.Show();

        var prompt = window.PromptAsync(new PromptOptions("Rename", "Name:", Input: "Okami"));
        Dispatcher.UIThread.RunJobs();
        var sheet = Assert.IsType<PromptSheet>(window.FindControl<Avalonia.Controls.ContentControl>("SheetHost")!.Content);
        Assert.False(window.FindControl<Avalonia.Controls.Grid>("Shell")!.IsEnabled);
        sheet.FindControl<Avalonia.Controls.TextBox>("InputBox")!.Text = " Okami HD ";
        sheet.FindControl<Avalonia.Controls.TextBox>("InputBox")!.Focus();
        window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Okami HD", (await prompt)!.Input);
        Assert.True(window.FindControl<Avalonia.Controls.Grid>("Shell")!.IsEnabled);
        window.Close();
    }

    /// <summary>
    /// Renders the UI to PNGs for eyeballing; set PS2ULIX_SNAPSHOT to an output folder.
    /// PS2ULIX_SNAPSHOT_ART=1 also downloads real covers for a fuller library.
    /// </summary>
    [AvaloniaFact]
    public async Task Windows_Render()
    {
        var folder = Environment.GetEnvironmentVariable("PS2ULIX_SNAPSHOT");
        var withArt = Environment.GetEnvironmentVariable("PS2ULIX_SNAPSHOT_ART") == "1";
        if (withArt)
            await AddDemoGamesAsync();
        new IsoFolders(_dir).AddGame(MakeIso(), name: "No Cover Game", format: IsoFormat.Zso);

        var window = new MainWindow { Width = 1280, Height = 800, ShowIntro = false };
        var vm = new MainViewModel(window);
        window.DataContext = vm;
        window.Show();
        vm.OpenFolder(_dir);
        await vm.ReloadAsync();

        void Snap(string name)
        {
            for (var i = 0; i < 80; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (!string.IsNullOrEmpty(folder))
                frame.Save(Path.Combine(folder, name + ".png"));
        }

        Snap("browser");
        vm.OpenDriveCommand.Execute(null);
        vm.SelectedGame = vm.VisibleGames[Math.Min(3, vm.VisibleGames.Count - 1)];
        Snap("drive");
        vm.OpenGameCommand.Execute(null);
        Snap("game");

        vm.IsBusy = true;
        vm.Status = "Adding Grand Theft Auto…";
        vm.Progress = 42;
        vm.JobDetail = "1.84 GiB of 4.38 GiB · 38.2 MiB/s · 1m 08s left";
        Snap("busy");
        vm.IsBusy = false;

        var add = window.PromptAsync(new PromptOptions("Add game", "GTA.iso\nSLUS_209.46 · 4.38 GiB\n\nGame name (max 32 chars, ASCII):",
            Input: "Grand Theft Auto", Media: UlMedia.Dvd, OkText: "Add", Choices: ["UL: split into 1 GiB parts", "ISO: one file", "ZSO: compressed"]));
        Snap("prompt");
        window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(await add);

        var settings = window.EditGameConfigAsync("OPL settings · Okami", new OplGameConfig { Compatibility = CompatModes.AccurateReads, Dma = 2, Vmc0 = "OKAMI" });
        Snap("settings");
        window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(await settings);

        // The start-up scene, frozen mid-way.
        var intro = window.FindControl<Ps2Ulix.Gui.Controls.BootIntro>("Intro")!;
        var layer = window.FindControl<Panel>("IntroLayer")!;
        layer.IsVisible = true;
        layer.Opacity = 1;
        foreach (var (time, name) in new[] { (0.55, "intro-swirl"), (1.12, "intro-flare"), (1.6, "intro-title") })
        {
            intro.Time = time;
            window.FindControl<TextBlock>("IntroTitle")!.Opacity = Ps2Ulix.Gui.Controls.BootIntro.TitleOpacityAt(time);
            Snap(name);
        }
        window.Close();
    }

    [AvaloniaFact]
    public async Task Navigation_BrowserDriveGame_AndBack()
    {
        var vm = await OpenAsync();
        Assert.True(vm.IsBrowserScreen);

        vm.OpenDriveCommand.Execute(null);
        Assert.True(vm.IsDriveScreen);
        vm.SelectedGame = vm.Games[0];
        vm.OpenGameCommand.Execute(null);
        Assert.True(vm.IsGameScreen);
        Assert.Equal("Okami", vm.HeaderTitle);

        vm.BackCommand.Execute(null);
        Assert.True(vm.IsDriveScreen);
        vm.SearchText = "ic";
        vm.BackCommand.Execute(null); // first clears the search
        Assert.True(vm.IsDriveScreen);
        Assert.Equal("", vm.SearchText);
        vm.BackCommand.Execute(null);
        Assert.True(vm.IsBrowserScreen);
    }

    [AvaloniaFact]
    public async Task Rename_OnGameScreen_StaysOnRenamedGame()
    {
        var vm = await OpenAsync();
        vm.OpenDriveCommand.Execute(null);
        vm.SelectedGame = vm.Games.Single(g => g.Name == "Okami");
        vm.OpenGameCommand.Execute(null);
        _dialogs.Answers.Enqueue(new PromptResult("Okami HD", null));

        await vm.RenameCommand.ExecuteAsync(null);

        Assert.True(vm.IsGameScreen);
        Assert.Equal("Okami HD", vm.SelectedGame?.Name);
    }

    [AvaloniaFact]
    public async Task Delete_OnGameScreen_ReturnsToDrive()
    {
        var vm = await OpenAsync();
        vm.OpenDriveCommand.Execute(null);
        vm.SelectedGame = vm.Games.Single(g => g.Name == "Ico");
        vm.OpenGameCommand.Execute(null);
        _dialogs.Answers.Enqueue(new PromptResult("", null));

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.True(vm.IsDriveScreen);
    }

    [AvaloniaFact]
    public async Task Window_TypingFiltersGames_EnterOpensGame()
    {
        var window = new MainWindow { ShowIntro = false };
        var vm = new MainViewModel(_dialogs);
        window.DataContext = vm;
        window.Show();
        vm.OpenFolder(_dir);
        await vm.ReloadAsync();
        vm.OpenDriveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        window.KeyTextInput("ic");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("ic", vm.SearchText);
        Assert.Equal(["Ico"], vm.VisibleGames.Select(g => g.Name));

        Assert.True(window.FindControl<TextBox>("SearchBox")!.IsFocused);

        // Enter in the search box goes to the results, Enter again opens the game.
        vm.SelectedGame = vm.VisibleGames[0];
        window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsDriveScreen);
        window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsGameScreen);

        window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsDriveScreen);
        window.Close();
    }

    private async Task AddDemoGamesAsync()
    {
        (string Name, string Id, UlMedia Media)[] demo =
        [
            ("Grand Theft Auto San Andreas", "SLUS_209.46", UlMedia.Dvd), ("Metal Gear Solid 3", "SLUS_208.15", UlMedia.Dvd),
            ("Shadow of the Colossus", "SCUS_974.72", UlMedia.Dvd), ("Final Fantasy X", "SLUS_203.12", UlMedia.Dvd),
            ("Okami Europe", "SLES_544.39", UlMedia.Dvd), ("Devil May Cry", "SLUS_202.16", UlMedia.Dvd),
            ("Kingdom Hearts", "SLUS_203.70", UlMedia.Dvd), ("Silent Hill 2", "SLUS_202.28", UlMedia.Dvd),
            ("God of War", "SCUS_974.74", UlMedia.Dvd), ("Ico Original", "SCUS_971.13", UlMedia.Cd),
        ];
        var drive = new UlDrive(_dir);
        var games = drive.LoadGames();
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ps2ulix");
        var art = new ArtDownloader(http);
        foreach (var (name, id, media) in demo)
        {
            var game = new UlGameEntry(name, id, 1, media);
            File.WriteAllBytes(drive.GetPartPath(game, 0), new byte[4096]);
            games.Add(game);
            await art.DownloadAsync(_dir, id, ["COV"]);
        }
        UlCfg.Save(drive.CfgPath, games);
    }
}
