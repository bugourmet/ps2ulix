using System.Buffers.Binary;

namespace Ps2Ulix.Core.Tests;

public sealed class ZsoTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ps2ulix-zso-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>ISO with a compressible half (zeros/text) and an incompressible half (random).</summary>
    private static byte[] MakeIso()
    {
        var random = new byte[20 * Iso9660.SectorSize];
        new Random(7).NextBytes(random);
        using var iso = new TestIsoBuilder { Udf = true }
            .AddFile("SYSTEM.CNF;1", "BOOT2 = cdrom0:\\SLES_544.39;1\r\n")
            .AddFile("ZEROS.BIN;1", new byte[20 * Iso9660.SectorSize])
            .AddFile("NOISE.BIN;1", random)
            .Build();
        return iso.ToArray();
    }

    private static byte[] Compress(byte[] iso)
    {
        using var output = new MemoryStream();
        Zso.Compress(new MemoryStream(iso), output);
        return output.ToArray();
    }

    [Fact]
    public void Compress_WritesOplHeaderAndIndex()
    {
        var iso = MakeIso();
        var zso = Compress(iso);
        var blocks = iso.Length / Zso.BlockSize;

        Assert.Equal(Zso.Magic, BinaryPrimitives.ReadUInt32LittleEndian(zso));
        Assert.Equal(0x18u, BinaryPrimitives.ReadUInt32LittleEndian(zso.AsSpan(4)));
        Assert.Equal((ulong)iso.Length, BinaryPrimitives.ReadUInt64LittleEndian(zso.AsSpan(8)));
        Assert.Equal(2048u, BinaryPrimitives.ReadUInt32LittleEndian(zso.AsSpan(16)));
        Assert.Equal(1, zso[20]);
        Assert.Equal(0, zso[21]);
        Assert.Equal((uint)(0x18 + (blocks + 1) * 4), BinaryPrimitives.ReadUInt32LittleEndian(zso.AsSpan(0x18)) & ~Zso.PlainFlag);
        Assert.Equal((uint)zso.Length, BinaryPrimitives.ReadUInt32LittleEndian(zso.AsSpan(0x18 + blocks * 4)));
        Assert.True(zso.Length < iso.Length);

        // Random blocks can't be compressed, so they are stored raw with the high bit set.
        var flags = Enumerable.Range(0, blocks).Select(b => BinaryPrimitives.ReadUInt32LittleEndian(zso.AsSpan(0x18 + b * 4)) & Zso.PlainFlag).ToList();
        Assert.Contains(Zso.PlainFlag, flags);
        Assert.Contains(0u, flags);
    }

    [Fact]
    public void Decompress_RoundTrips()
    {
        var iso = MakeIso();
        using var output = new MemoryStream();

        Zso.Decompress(new MemoryStream(Compress(iso)), output);

        Assert.Equal(iso, output.ToArray());
    }

    [Fact]
    public void Reader_SeeksAcrossBlocks()
    {
        var iso = MakeIso();
        using var reader = new ZsoReader(new MemoryStream(Compress(iso)));
        var buffer = new byte[5000];

        reader.Position = 30_000;
        reader.ReadExactly(buffer);

        Assert.Equal(iso.AsSpan(30_000, 5000).ToArray(), buffer);
        Assert.Equal(iso.Length, reader.Length);
    }

    [Fact]
    public void Ps2IsoRead_DetectsZsoByContent()
    {
        var path = Path.Combine(_dir, "game.zso");
        File.WriteAllBytes(path, Compress(MakeIso()));

        var info = Ps2Iso.Read(path);

        Assert.Equal("SLES_544.39", info.GameId);
        Assert.Equal(UlMedia.Dvd, info.Media);
        Assert.Equal(MakeIso().Length, info.Size);
    }

    [Fact]
    public void Compress_UnalignedIso_Throws()
    {
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => Zso.Compress(new MemoryStream(new byte[3000]), output));
    }

    [Fact]
    public void Reader_NotZso_Throws()
    {
        Assert.Throws<InvalidDataException>(() => new ZsoReader(new MemoryStream(MakeIso())));
    }
}
