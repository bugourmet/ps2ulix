using System.Text;

namespace Ps2Ulix.Core;

/// <summary>What ps2ulix needs to know about a PS2 ISO before converting it.</summary>
public sealed record Ps2IsoInfo(string GameId, UlMedia Media, string VolumeId, long Size);

public static class Ps2Iso
{
    /// <summary>80-min CD capacity; anything bigger must be a DVD.</summary>
    public const long MaxCdSectors = 360_000;

    /// <summary>Reads a .iso or .zso (detected by content). Size is the uncompressed ISO size.</summary>
    public static Ps2IsoInfo Read(string path)
    {
        using var stream = OpenImage(path);
        return Read(stream);
    }

    /// <summary>Opens a disc image as plain ISO bytes; ZSO files are decompressed on the fly.</summary>
    public static Stream OpenImage(string path)
    {
        var file = File.OpenRead(path);
        try
        {
            return Zso.IsZso(file) ? new ZsoReader(file) : file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <exception cref="InvalidDataException">Not a PS2 ISO.</exception>
    public static Ps2IsoInfo Read(Stream stream)
    {
        var iso = Iso9660.Open(stream);
        var cnf = iso.ReadRootFile(SystemCnf.FileName)
            ?? throw new InvalidDataException("No SYSTEM.CNF in root; not a PS2 game disc.");
        var gameId = SystemCnf.ParseGameId(Encoding.ASCII.GetString(cnf));
        return new Ps2IsoInfo(gameId, DetectMedia(iso, stream.Length), iso.VolumeId, stream.Length);
    }

    /// <summary>
    /// PS2 DVDs are UDF/ISO bridge discs; PS2 CDs are plain ISO9660. Size is the fallback for
    /// images rebuilt without UDF. Small DVD images without UDF are misdetected as CD; UI should allow override.
    /// </summary>
    public static UlMedia DetectMedia(Iso9660 iso, long imageSize)
    {
        var sectors = Math.Max(iso.VolumeSectors, imageSize / Iso9660.SectorSize);
        return iso.HasUdf || sectors > MaxCdSectors ? UlMedia.Dvd : UlMedia.Cd;
    }
}
