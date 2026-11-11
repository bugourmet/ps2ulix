namespace Ps2Ulix.Core.Tests;

public sealed class UlCfgTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ps2ulix-tests-").FullName;

    private static readonly UlGameEntry[] Games =
    [
        new("Okami", "SLES_544.39", 4, UlMedia.Dvd),
        new("Ico", "SCUS_971.13", 1, UlMedia.Cd),
    ];

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        using var stream = new MemoryStream();
        UlCfg.Write(stream, Games);
        Assert.Equal(128, stream.Length);

        stream.Position = 0;
        Assert.Equal(Games, UlCfg.Read(stream));
    }

    [Fact]
    public void Read_IgnoresTrailingPartialRecord()
    {
        using var stream = new MemoryStream();
        UlCfg.Write(stream, Games);
        stream.Write(new byte[10]);

        stream.Position = 0;
        Assert.Equal(2, UlCfg.Read(stream).Count);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(UlCfg.Load(Path.Combine(_dir, UlCfg.FileName)));
    }

    [Fact]
    public void Save_ReplacesFileAndRemovesTemp()
    {
        var path = Path.Combine(_dir, UlCfg.FileName);
        File.WriteAllBytes(path, new byte[64 * 5]);

        UlCfg.Save(path, Games);

        Assert.Equal(Games, UlCfg.Load(path));
        Assert.False(File.Exists(path + ".new"));
    }
}
