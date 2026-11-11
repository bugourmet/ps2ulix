namespace Ps2Ulix.Core.Tests;

public sealed class IsoFoldersTests : DriveTestBase
{
    private IsoFolders Folders => new(_driveDir);

    [Fact]
    public void AddGame_CopiesIntoMediaFolder()
    {
        var iso = MakeIso("SLUS_209.46.Grand Theft Auto.iso");

        var game = Folders.AddGame(iso);

        Assert.Equal(Path.Combine(_driveDir, "CD", "Grand Theft Auto.iso"), game.Path);
        Assert.Equal(File.ReadAllBytes(iso), File.ReadAllBytes(game.Path));
        Assert.Equal(new IsoGame(game.Path, "Grand Theft Auto", "SLUS_209.46", UlMedia.Cd, IsoFormat.Iso, new FileInfo(iso).Length),
            Assert.Single(Folders.LoadGames()));
    }

    [Fact]
    public void AddGame_Zso_IsReadableAndSmaller()
    {
        var iso = MakeIso("Okami.iso", "SLES_544.39", payload: 0);
        File.AppendAllBytes(iso, new byte[50 * Iso9660.SectorSize]); // compressible tail

        var game = Folders.AddGame(iso, name: "Okami", media: UlMedia.Dvd, format: IsoFormat.Zso);

        Assert.Equal(Path.Combine(_driveDir, "DVD", "Okami.zso"), game.Path);
        Assert.True(game.FileSize < new FileInfo(iso).Length);
        Assert.Equal("SLES_544.39", Assert.Single(Folders.LoadGames()).GameId);
        using var image = Ps2Iso.OpenImage(game.Path);
        using var copy = new MemoryStream();
        image.CopyTo(copy);
        Assert.Equal(File.ReadAllBytes(iso), copy.ToArray());
    }

    [Fact]
    public void LoadGames_OldStyleNames_IgnoresOtherFiles()
    {
        Directory.CreateDirectory(Path.Combine(_driveDir, "DVD"));
        File.WriteAllText(Path.Combine(_driveDir, "DVD", "SLES_544.39.Okami.iso"), "not a real iso");
        File.WriteAllText(Path.Combine(_driveDir, "DVD", "Broken.iso"), "junk");
        File.WriteAllText(Path.Combine(_driveDir, "DVD", "notes.txt"), "x");

        var games = Folders.LoadGames();

        Assert.Equal([("Broken", (string?)null), ("Okami", "SLES_544.39")], games.Select(g => (g.Name, g.GameId)));
    }

    [Fact]
    public void AddGame_Exists_ThrowsAndKeepsFile()
    {
        var iso = MakeIso("Okami.iso");
        var game = Folders.AddGame(iso);
        File.WriteAllText(game.Path, "keep");

        Assert.Throws<IOException>(() => Folders.AddGame(iso));
        Assert.Equal("keep", File.ReadAllText(game.Path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(game.Path)!, "*.part"));
    }

    [Fact]
    public void AddGame_Cancelled_LeavesNoFile()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Folders.AddGame(MakeIso("Okami.iso", payload: 3_000_000), cancellationToken: cts.Token));
        Assert.Empty(Directory.GetFiles(Path.Combine(_driveDir, "CD")));
    }

    [Theory]
    [InlineData("Bad:Name")]
    [InlineData(" Padded")]
    [InlineData("Dot.")]
    [InlineData("Ōkami")]
    public void AddGame_InvalidName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Folders.AddGame(MakeIso("x.iso"), name: name));
    }

    [Fact]
    public void RenameAndDelete()
    {
        var game = Folders.AddGame(MakeIso("Okami.iso"));

        var renamed = Folders.RenameGame(game, "Okami HD");
        Assert.Equal("Okami HD", Assert.Single(Folders.LoadGames()).Name);

        Folders.DeleteGame(renamed);
        Assert.Empty(Folders.LoadGames());
    }

    [Fact]
    public void ConvertUlToZso_ViaGameImage()
    {
        var iso = MakeIso("Okami.iso");
        var ul = _drive.AddGame(iso);
        Ps2IsoInfo info;
        using (var image = _drive.OpenGameImage(ul))
            info = Ps2Iso.Read(image);

        var game = Folders.AddGame(() => _drive.OpenGameImage(ul), info, ul.Name, ul.Media, IsoFormat.Zso);

        using var back = Ps2Iso.OpenImage(game.Path);
        using var copy = new MemoryStream();
        back.CopyTo(copy);
        Assert.Equal(File.ReadAllBytes(iso), copy.ToArray());
    }
}
