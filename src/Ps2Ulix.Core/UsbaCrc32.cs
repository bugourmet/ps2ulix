namespace Ps2Ulix.Core;

/// <summary>
/// Non-standard CRC32 used by USBAdvance/USBUtil/OPL to name UL part files.
/// Port of <c>USBA_crc32</c> from OPL <c>src/system.c</c>.
/// </summary>
public static class UsbaCrc32
{
    public const int MaxNameLength = 32;

    private static readonly int[] Table = new int[256];

    // OPL never resets crc after building the table, so hashing starts from the last value computed.
    private static readonly int InitialCrc;

    static UsbaCrc32()
    {
        var crc = 0;
        for (var i = 0; i < 256; i++)
        {
            crc = i << 24;
            for (var bit = 0; bit < 8; bit++)
                crc = crc < 0 ? crc << 1 : (crc << 1) ^ 0x04C11DB7;
            Table[255 - i] = crc;
        }
        InitialCrc = crc;
    }

    /// <summary>
    /// Hashes the game name like OPL: chars up to the first NUL (max 32), then the NUL itself.
    /// </summary>
    /// <exception cref="ArgumentException">Non-ASCII char; OPL indexes its table with signed char, undefined for those.</exception>
    public static uint Compute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var crc = InitialCrc;
        foreach (var c in name.AsSpan(0, Math.Min(name.Length, MaxNameLength)))
        {
            if (c == '\0')
                break;
            if (c > 0x7F)
                throw new ArgumentException("Game name must be ASCII.", nameof(name));
            crc = Step(crc, c);
        }
        return unchecked((uint)Step(crc, 0));
    }

    private static int Step(int crc, int b) => Table[b ^ ((crc >> 24) & 0xFF)] ^ (crc << 8);
}
