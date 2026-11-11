using System.Text;

namespace Ps2Ulix.Core;

/// <summary>Disc type byte in ul.cfg (ps2sdk SCECdPS2CD / SCECdPS2DVD).</summary>
public enum UlMedia : byte
{
    Cd = 0x12,
    Dvd = 0x14,
}

/// <summary>One 64-byte ul.cfg record (OPL <c>USBExtreme_game_entry_t</c>).</summary>
public sealed record UlGameEntry
{
    public const int Size = 64;
    public const int NameLength = 32;
    public const int StartupLength = 12;

    private const int MagicOffset = 0x20;
    private const int StartupOffset = 0x23;
    private const int PartsOffset = 0x2F;
    private const int MediaOffset = 0x30;
    private const int Byte08Offset = 0x35;
    private static ReadOnlySpan<byte> Magic => "ul."u8;
    private const string FatInvalidChars = "\"*/:<>?\\|";

    public UlGameEntry(string name, string startup, byte parts, UlMedia media)
    {
        Name = ValidateText(name, NameLength, nameof(name));
        Startup = ValidateText(startup, StartupLength, nameof(startup));
        if (Startup.AsSpan().IndexOfAny(FatInvalidChars) >= 0)
            throw new ArgumentException("Startup id has chars not allowed in FAT file names.", nameof(startup));
        Parts = parts;
        Media = media;
    }

    public string Name { get; }

    /// <summary>Boot ELF id, e.g. <c>SLUS_123.45</c>.</summary>
    public string Startup { get; }

    public byte Parts { get; }

    public UlMedia Media { get; }

    public uint NameCrc => UsbaCrc32.Compute(Name);

    /// <summary>Part file name, OPL format <c>ul.%08X.%s.%02x</c>.</summary>
    public string GetPartFileName(int part)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(part);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(part, 256);
        return $"ul.{NameCrc:X8}.{Startup}.{part:x2}";
    }

    public IEnumerable<string> GetPartFileNames() => Enumerable.Range(0, Parts).Select(GetPartFileName);

    /// <summary>Parses <c>ul.&lt;CRC&gt;.&lt;startup&gt;.&lt;part&gt;</c>; false for any other file name.</summary>
    public static bool TryParsePartFileName(string fileName, out uint nameCrc, out string startup, out int part)
    {
        nameCrc = 0;
        startup = "";
        part = 0;
        // "ul." + 8 hex + "." + startup + "." + 2 hex
        if (fileName.Length < 3 + 8 + 1 + 1 + 1 + 2 || !fileName.StartsWith("ul.", StringComparison.OrdinalIgnoreCase)
            || fileName[11] != '.' || fileName[^3] != '.')
            return false;
        if (!uint.TryParse(fileName.AsSpan(3, 8), System.Globalization.NumberStyles.AllowHexSpecifier, null, out nameCrc)
            || !int.TryParse(fileName.AsSpan(fileName.Length - 2), System.Globalization.NumberStyles.AllowHexSpecifier, null, out part))
            return false;
        startup = fileName[12..^3];
        return startup.Length is > 0 and <= StartupLength;
    }

    public void WriteTo(Span<byte> record)
    {
        if (record.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(record));
        var r = record[..Size];
        r.Clear();
        Encoding.ASCII.GetBytes(Name, r[..NameLength]);
        Magic.CopyTo(r[MagicOffset..]);
        Encoding.ASCII.GetBytes(Startup, r.Slice(StartupOffset, StartupLength));
        r[PartsOffset] = Parts;
        r[MediaOffset] = (byte)Media;
        r[Byte08Offset] = 0x08;
    }

    /// <summary>Parses a record. Like OPL, ignores magic and padding bytes.</summary>
    public static UlGameEntry Parse(ReadOnlySpan<byte> record)
    {
        if (record.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(record));
        return new UlGameEntry(
            ReadText(record[..NameLength]),
            ReadText(record.Slice(StartupOffset, StartupLength)),
            record[PartsOffset],
            (UlMedia)record[MediaOffset]);
    }

    private static string ReadText(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? field : field[..end]);
    }

    private static string ValidateText(string value, int maxLength, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (value.Length == 0)
            throw new ArgumentException("Must not be empty.", paramName);
        if (value.Length > maxLength)
            throw new ArgumentException($"Max {maxLength} chars.", paramName);
        foreach (var c in value)
        {
            if (c is < ' ' or > '~')
                throw new ArgumentException("Only printable ASCII allowed.", paramName);
        }
        return value;
    }
}
