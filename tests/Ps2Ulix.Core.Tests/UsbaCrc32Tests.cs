namespace Ps2Ulix.Core.Tests;

public class UsbaCrc32Tests
{
    // Expected values from a separate literal Python port of OPL's C USBA_crc32 (int32 semantics).
    // TODO: add vectors from real files made by USBUtil/OPL once available.
    [Theory]
    [InlineData("A", 0x935FAD90u)]
    [InlineData("Okami", 0x4C2EA80Eu)]
    [InlineData("Grand Theft Auto San Andreas", 0x150FFDE8u)]
    [InlineData("Metal Gear Solid 3 Snake Eater", 0x1E619BEFu)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ012345", 0xE221E678u)]
    public void Compute_MatchesOplReference(string name, uint expected)
    {
        Assert.Equal(expected, UsbaCrc32.Compute(name));
    }

    [Fact]
    public void Compute_IgnoresCharsPast32()
    {
        Assert.Equal(
            UsbaCrc32.Compute("ABCDEFGHIJKLMNOPQRSTUVWXYZ012345"),
            UsbaCrc32.Compute("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"));
    }

    [Fact]
    public void Compute_StopsAtNul()
    {
        Assert.Equal(UsbaCrc32.Compute("Okami"), UsbaCrc32.Compute("Okami\0junk"));
    }

    [Fact]
    public void Compute_RejectsNonAscii()
    {
        Assert.Throws<ArgumentException>(() => UsbaCrc32.Compute("Ōkami"));
    }
}
