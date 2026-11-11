namespace Ps2Ulix.Core.Tests;

public sealed class OplGameConfigTests : IDisposable
{
    private const string Id = "SLUS_209.46";
    private readonly string _root = Directory.CreateTempSubdirectory("ps2ulix-cfg-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string CfgPath => OplGameConfig.GetPath(_root, Id);

    [Fact]
    public void Load_Missing_IsEmptyWithDefaults()
    {
        var config = OplGameConfig.Load(_root, Id);

        Assert.Empty(config.Entries);
        Assert.Equal(CompatModes.None, config.Compatibility);
        Assert.Equal(7, config.Dma);
    }

    [Fact]
    public void Save_WritesOplFormat()
    {
        var config = new OplGameConfig
        {
            Compatibility = CompatModes.AccurateReads | CompatModes.SkipVideos,
            Dma = 2,
            Vmc0 = "GTA",
        };

        config.Save(_root, Id);

        Assert.Equal(Path.Combine(_root, "CFG", "SLUS_209.46.cfg"), CfgPath);
        Assert.Equal("$Compatibility=9\r\n$DMA=2\r\n$VMC_0=GTA\r\n", File.ReadAllText(CfgPath));
    }

    [Fact]
    public void Load_KeepsUnknownKeys_DropsHashKeysOnSave()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CfgPath)!);
        File.WriteAllText(CfgPath, "#Name=GTA\r\n  $EnableGSM=1\r\ngarbage line\r\n$GSMVMode=a=b\r\n$Compatibility=5\r\n");

        var config = OplGameConfig.Load(_root, Id);
        config.Compatibility |= CompatModes.SyncReads;
        config.Save(_root, Id);

        Assert.Equal("a=b", config.Get("$GSMVMode"));
        Assert.Equal("$EnableGSM=1\r\n$GSMVMode=a=b\r\n$Compatibility=7\r\n", File.ReadAllText(CfgPath));
    }

    [Fact]
    public void DefaultsRemoveKeys_EmptyConfigDeletesFile()
    {
        var config = new OplGameConfig { Compatibility = CompatModes.DisableIgr, Dma = 0, AltStartup = "SLUS_999.99" };
        config.Save(_root, Id);

        config.Compatibility = CompatModes.None;
        config.Dma = 7;
        config.AltStartup = "";
        config.Save(_root, Id);

        Assert.False(File.Exists(CfgPath));
    }

    [Theory]
    [InlineData("$DMA=9", 7)]
    [InlineData("$DMA=x", 7)]
    [InlineData("$DMA=3", 3)]
    public void Dma_InvalidStoredValue_FallsBackToDefault(string line, int expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CfgPath)!);
        File.WriteAllText(CfgPath, line + "\r\n");

        Assert.Equal(expected, OplGameConfig.Load(_root, Id).Dma);
    }

    [Fact]
    public void Set_RejectsBadKeysAndValues()
    {
        var config = new OplGameConfig();
        Assert.Throws<ArgumentException>(() => config.Set("a=b", "1"));
        Assert.Throws<ArgumentException>(() => config.Set("$VMC_0", "line\nbreak"));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.Dma = 8);
    }
}
