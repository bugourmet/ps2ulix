namespace Ps2Ulix.Core.Tests;

public class UlGameEntryTests
{
    private static readonly UlGameEntry Okami = new("Okami", "SLES_544.39", 3, UlMedia.Dvd);

    [Fact]
    public void WriteTo_ProducesOplLayout()
    {
        var record = new byte[UlGameEntry.Size];
        Okami.WriteTo(record);

        var expected = new byte[64];
        "Okami"u8.CopyTo(expected);
        "ul."u8.CopyTo(expected.AsSpan(0x20));
        "SLES_544.39"u8.CopyTo(expected.AsSpan(0x23));
        expected[0x2F] = 3;
        expected[0x30] = 0x14;
        expected[0x35] = 0x08;
        Assert.Equal(expected, record);
    }

    [Fact]
    public void WriteTo_FullLengthFieldsHaveNoNul()
    {
        var entry = new UlGameEntry(new string('X', 32), "ABCD_123.456", 1, UlMedia.Cd);
        var record = new byte[UlGameEntry.Size];
        entry.WriteTo(record);

        Assert.Equal((byte)'u', record[0x20]);
        Assert.Equal((byte)'6', record[0x2E]);
        Assert.Equal(entry, UlGameEntry.Parse(record));
    }

    [Fact]
    public void Parse_RoundTrips()
    {
        var record = new byte[UlGameEntry.Size];
        Okami.WriteTo(record);
        Assert.Equal(Okami, UlGameEntry.Parse(record));
    }

    [Fact]
    public void GetPartFileNames_UsesOplFormat()
    {
        Assert.Equal(
            ["ul.4C2EA80E.SLES_544.39.00", "ul.4C2EA80E.SLES_544.39.01", "ul.4C2EA80E.SLES_544.39.02"],
            Okami.GetPartFileNames());
        Assert.Equal("ul.4C2EA80E.SLES_544.39.0a", Okami.GetPartFileName(10));
    }

    [Theory]
    [InlineData("ul.4C2EA80E.SLES_544.39.0a", true, 0x4C2EA80Eu, "SLES_544.39", 10)]
    [InlineData("UL.4c2ea80e.SLES_544.39.00", true, 0x4C2EA80Eu, "SLES_544.39", 0)]
    [InlineData("ul.cfg", false, 0u, "", 0)]
    [InlineData("ul.4C2EA80E.SLES_544.39.0", false, 0u, "", 0)]
    [InlineData("ul.4C2EA80X.SLES_544.39.00", false, 0u, "", 0)]
    [InlineData("ul.4C2EA80E..00", false, 0u, "", 0)]
    public void TryParsePartFileName(string file, bool ok, uint crc, string startup, int part)
    {
        Assert.Equal(ok, UlGameEntry.TryParsePartFileName(file, out var c, out var s, out var p));
        if (ok)
            Assert.Equal((crc, startup, part), (c, s, p));
    }

    [Theory]
    [InlineData("", "SLUS_123.45")]
    [InlineData("123456789012345678901234567890123", "SLUS_123.45")]
    [InlineData("Ōkami", "SLUS_123.45")]
    [InlineData("Okami", "")]
    [InlineData("Okami", "SLUS_123.456X")]
    [InlineData("Okami", "SLUS:123.45")]
    public void Ctor_RejectsInvalidFields(string name, string startup)
    {
        Assert.Throws<ArgumentException>(() => new UlGameEntry(name, startup, 1, UlMedia.Cd));
    }
}
