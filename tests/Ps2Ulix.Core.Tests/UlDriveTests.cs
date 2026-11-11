namespace Ps2Ulix.Core.Tests;

public sealed class UlDriveTests : DriveTestBase
{
    [Fact]
    public void AddGame_SplitsIsoAndAddsEntry()
    {
        var iso = MakeIso("Grand Theft Auto.iso");
        var isoSize = new FileInfo(iso).Length;
        long lastProgress = 0;

        var game = _drive.AddGame(iso, progress: new SyncProgress(p => lastProgress = p));

        var expectedParts = (int)((isoSize + PartSize - 1) / PartSize);
        Assert.Equal(new UlGameEntry("Grand Theft Auto", "SLUS_209.46", (byte)expectedParts, UlMedia.Cd), game);
        Assert.Equal([game], _drive.LoadGames());
        Assert.Equal(game.GetPartFileNames().Order(), PartFiles());
        Assert.All(Enumerable.Range(0, expectedParts - 1), p => Assert.Equal(PartSize, new FileInfo(_drive.GetPartPath(game, p)).Length));
        Assert.Equal(File.ReadAllBytes(iso), Enumerable.Range(0, expectedParts).SelectMany(p => File.ReadAllBytes(_drive.GetPartPath(game, p))));
        Assert.Equal(isoSize, lastProgress);
    }

    [Fact]
    public void AddGame_UsesGivenNameAndMedia()
    {
        var game = _drive.AddGame(MakeIso("x.iso"), name: "My Game", media: UlMedia.Dvd);
        Assert.Equal("My Game", game.Name);
        Assert.Equal(UlMedia.Dvd, game.Media);
    }

    [Fact]
    public void AddGame_SameNameAndId_ThrowsAndWritesNothing()
    {
        var iso = MakeIso("Okami.iso");
        _drive.AddGame(iso);
        var before = PartFiles();

        Assert.Throws<InvalidOperationException>(() => _drive.AddGame(iso));
        Assert.Equal(before, PartFiles());
        Assert.Single(_drive.LoadGames());
    }

    [Fact]
    public void AddGame_Cancelled_RemovesPartsAndKeepsCfg()
    {
        _drive.AddGame(MakeIso("Ico.iso", "SCUS_971.13"));
        var before = PartFiles();
        using var cts = new CancellationTokenSource();

        Assert.Throws<OperationCanceledException>(() =>
            _drive.AddGame(MakeIso("Okami.iso"), progress: new SyncProgress(p => { if (p > PartSize) cts.Cancel(); }), cancellationToken: cts.Token));

        Assert.Equal(before, PartFiles());
        Assert.Single(_drive.LoadGames());
    }

    [Fact]
    public void JoinGame_RebuildsOriginalIso()
    {
        var iso = MakeIso("Okami.iso");
        var game = _drive.AddGame(iso);
        var output = Path.Combine(_work, "out.iso");

        _drive.JoinGame(game, output);

        Assert.Equal(File.ReadAllBytes(iso), File.ReadAllBytes(output));
    }

    [Fact]
    public void JoinGame_MissingPart_ThrowsAndWritesNothing()
    {
        var game = _drive.AddGame(MakeIso("Okami.iso"));
        File.Delete(_drive.GetPartPath(game, 1));
        var output = Path.Combine(_work, "out.iso");

        var ex = Assert.Throws<FileNotFoundException>(() => _drive.JoinGame(game, output));
        Assert.Contains(game.GetPartFileName(1), ex.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void JoinGame_OutputExists_ThrowsAndKeepsFile()
    {
        var game = _drive.AddGame(MakeIso("Okami.iso"));
        var output = Path.Combine(_work, "out.iso");
        File.WriteAllText(output, "keep");

        Assert.Throws<IOException>(() => _drive.JoinGame(game, output));
        Assert.Equal("keep", File.ReadAllText(output));
    }

    [Fact]
    public void DeleteGame_RemovesEntryAndParts_KeepsOthers()
    {
        var ico = _drive.AddGame(MakeIso("Ico.iso", "SCUS_971.13"));
        var okami = _drive.AddGame(MakeIso("Okami.iso"));

        _drive.DeleteGame(okami);

        Assert.Equal([ico], _drive.LoadGames());
        Assert.Equal(ico.GetPartFileNames().Order(), PartFiles());
    }

    [Fact]
    public void RenameGame_RenamesPartsAndEntry()
    {
        var iso = MakeIso("Okami.iso");
        var game = _drive.AddGame(iso);

        var renamed = _drive.RenameGame(game, "Okami HD");

        Assert.Equal("Okami HD", renamed.Name);
        Assert.Equal([renamed], _drive.LoadGames());
        Assert.Equal(renamed.GetPartFileNames().Order(), PartFiles());
        var output = Path.Combine(_work, "out.iso");
        _drive.JoinGame(renamed, output);
        Assert.Equal(File.ReadAllBytes(iso), File.ReadAllBytes(output));
    }

    [Fact]
    public void RenameGame_TargetPartExists_ThrowsAndChangesNothing()
    {
        var game = _drive.AddGame(MakeIso("Okami.iso"));
        var target = new UlGameEntry("Okami HD", game.Startup, game.Parts, game.Media);
        File.WriteAllText(_drive.GetPartPath(target, game.Parts - 1), "blocker");
        var before = PartFiles();

        Assert.Throws<IOException>(() => _drive.RenameGame(game, "Okami HD"));

        Assert.Equal(before, PartFiles());
        Assert.Equal([game], _drive.LoadGames());
    }

    [Fact]
    public void FindGame_ByNameOrId()
    {
        var ico = _drive.AddGame(MakeIso("Ico.iso", "SCUS_971.13"));
        var okami = _drive.AddGame(MakeIso("Okami.iso"));
        _drive.AddGame(MakeIso("Okami Copy.iso"));

        Assert.Equal(ico, _drive.FindGame("scus_971.13"));
        Assert.Equal(okami, _drive.FindGame("okami"));
        Assert.Throws<InvalidOperationException>(() => _drive.FindGame("SLUS_209.46"));
        Assert.Throws<InvalidOperationException>(() => _drive.FindGame("nope"));
    }

    [Theory]
    [InlineData("Okami.iso", "Okami")]
    [InlineData("SLES_544.39.Okami.iso", "Okami")]
    [InlineData("Pokémon Ünïcode.iso", "Pokemon Unicode")]
    [InlineData("日本.iso", "__")]
    [InlineData("A Very Long Game Name That Goes Past The Limit.iso", "A Very Long Game Name That Goes")]
    public void NameFromFile_MakesValidName(string file, string expected)
    {
        Assert.Equal(expected, UlDrive.NameFromFile(file));
    }
}
