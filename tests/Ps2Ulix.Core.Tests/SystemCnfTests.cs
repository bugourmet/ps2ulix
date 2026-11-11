namespace Ps2Ulix.Core.Tests;

public class SystemCnfTests
{
    [Theory]
    [InlineData("BOOT2 = cdrom0:\\SLUS_209.46;1\r\nVER = 1.00\r\nVMODE = NTSC\r\n", "SLUS_209.46")]
    [InlineData("BOOT2=cdrom0:\\SCES_500.51;1", "SCES_500.51")]
    [InlineData("boot2 = cdrom0:\\slpm_123.45;1", "SLPM_123.45")]
    [InlineData("BOOT2 = cdrom0:\\SLES_544.39", "SLES_544.39")]
    [InlineData("BOOT2 = cdrom0:\\DATA\\SLPS_250.01;1", "SLPS_250.01")]
    [InlineData("VER = 1.00\nBOOT2 = cdrom0:\\SLUS_212.42;1\0\0", "SLUS_212.42")]
    public void ParseGameId_ExtractsElfName(string text, string expected)
    {
        Assert.Equal(expected, SystemCnf.ParseGameId(text));
    }

    [Fact]
    public void ParseGameId_Ps1Disc_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => SystemCnf.ParseGameId("BOOT = cdrom:\\SLUS_000.67;1\r\nTCB = 4\r\n"));
        Assert.Contains("PS1", ex.Message);
    }

    [Theory]
    [InlineData("VER = 1.00")]
    [InlineData("BOOT2 = cdrom0:\\;1")]
    public void ParseGameId_NoUsableBoot2_Throws(string text)
    {
        Assert.Throws<InvalidDataException>(() => SystemCnf.ParseGameId(text));
    }
}
