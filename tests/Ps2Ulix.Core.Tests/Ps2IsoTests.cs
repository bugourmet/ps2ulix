namespace Ps2Ulix.Core.Tests;

public class Ps2IsoTests
{
    private const string Cnf = "BOOT2 = cdrom0:\\SLUS_209.46;1\r\nVER = 1.00\r\n";

    [Fact]
    public void Read_PlainIso_IsCdWithGameId()
    {
        using var iso = new TestIsoBuilder { VolumeId = "SLUS_20946" }
            .AddFile("SYSTEM.CNF;1", Cnf)
            .AddFile("SLUS_209.46;1", new byte[5000])
            .Build();

        var info = Ps2Iso.Read(iso);

        Assert.Equal(new Ps2IsoInfo("SLUS_209.46", UlMedia.Cd, "SLUS_20946", iso.Length), info);
    }

    [Fact]
    public void Read_UdfBridge_IsDvd()
    {
        using var iso = new TestIsoBuilder { Udf = true }.AddFile("SYSTEM.CNF;1", Cnf).Build();
        Assert.Equal(UlMedia.Dvd, Ps2Iso.Read(iso).Media);
    }

    [Fact]
    public void Read_VolumeBiggerThanCd_IsDvd()
    {
        using var iso = new TestIsoBuilder { VolumeSectors = 360_001 }.AddFile("SYSTEM.CNF;1", Cnf).Build();
        Assert.Equal(UlMedia.Dvd, Ps2Iso.Read(iso).Media);
    }

    [Fact]
    public void Read_FindsSystemCnfInLaterRootSector()
    {
        var builder = new TestIsoBuilder();
        for (var i = 0; i < 80; i++)
            builder.AddFile($"FILLER{i:D2}.BIN;1", new byte[1]);
        using var iso = builder.AddFile("system.cnf;1", Cnf).Build();

        Assert.True(iso.Length > 30 * Iso9660.SectorSize);
        Assert.Equal("SLUS_209.46", Ps2Iso.Read(iso).GameId);
    }

    [Fact]
    public void Read_IgnoresDirectoryNamedSystemCnf()
    {
        using var iso = new TestIsoBuilder().AddDirectory("SYSTEM.CNF").Build();
        var ex = Assert.Throws<InvalidDataException>(() => Ps2Iso.Read(iso));
        Assert.Contains("SYSTEM.CNF", ex.Message);
    }

    [Fact]
    public void Read_NoSystemCnf_Throws()
    {
        using var iso = new TestIsoBuilder().AddFile("README.TXT;1", "hi").Build();
        Assert.Throws<InvalidDataException>(() => Ps2Iso.Read(iso));
    }

    [Fact]
    public void Read_NotIso_Throws()
    {
        using var stream = new MemoryStream(new byte[40 * Iso9660.SectorSize]);
        Assert.Throws<InvalidDataException>(() => Ps2Iso.Read(stream));
    }

    [Fact]
    public void Read_RawBinImage_ThrowsWithHint()
    {
        var data = new byte[40 * Iso9660.SectorSize];
        byte[] sync = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
        sync.CopyTo(data, 0);
        using var stream = new MemoryStream(data);

        var ex = Assert.Throws<InvalidDataException>(() => Ps2Iso.Read(stream));
        Assert.Contains("2352", ex.Message);
    }

    [Fact]
    public void Read_FromFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var iso = new TestIsoBuilder().AddFile("SYSTEM.CNF;1", Cnf).Build())
                File.WriteAllBytes(path, iso.ToArray());
            Assert.Equal("SLUS_209.46", Ps2Iso.Read(path).GameId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
