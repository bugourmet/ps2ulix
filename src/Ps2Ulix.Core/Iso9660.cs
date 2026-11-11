using System.Buffers.Binary;
using System.Text;

namespace Ps2Ulix.Core;

/// <summary>Minimal read-only ISO9660 access: volume descriptors and files in the root directory.</summary>
public sealed class Iso9660
{
    public const int SectorSize = 2048;
    private const int DescriptorStartSector = 16;
    private const int MaxDescriptors = 64;
    private const int MaxRootFileSize = 64 * 1024;

    private static ReadOnlySpan<byte> RawSectorSync => [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    private readonly Stream _stream;
    private readonly uint _rootLba;
    private readonly uint _rootSize;

    private Iso9660(Stream stream, string volumeId, uint volumeSectors, uint rootLba, uint rootSize, bool hasUdf)
    {
        _stream = stream;
        VolumeId = volumeId;
        VolumeSectors = volumeSectors;
        _rootLba = rootLba;
        _rootSize = rootSize;
        HasUdf = hasUdf;
    }

    public string VolumeId { get; }

    /// <summary>Volume size in sectors, as stated by the primary volume descriptor.</summary>
    public uint VolumeSectors { get; }

    /// <summary>UDF descriptors (BEA01/NSR0x) found after the ISO descriptors; PS2 DVDs are UDF/ISO bridge discs.</summary>
    public bool HasUdf { get; }

    /// <exception cref="InvalidDataException">Not a 2048-byte-sector ISO9660 image.</exception>
    public static Iso9660 Open(Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));

        var sector = new byte[SectorSize];
        if (TryReadSector(stream, 0, sector) && sector.AsSpan().StartsWith(RawSectorSync))
            throw new InvalidDataException("Raw 2352-byte sector image (BIN); convert to 2048-byte ISO first.");

        string? volumeId = null;
        uint volumeSectors = 0, rootLba = 0, rootSize = 0;
        var hasUdf = false;
        for (var i = 0; i < MaxDescriptors; i++)
        {
            if (!TryReadSector(stream, DescriptorStartSector + i, sector))
                break;
            var id = Encoding.ASCII.GetString(sector, 1, 5);
            if (id is "BEA01" or "NSR02" or "NSR03")
            {
                hasUdf = true;
                continue;
            }
            if (id == "TEA01")
                break;
            if (id != "CD001")
            {
                if (volumeId is null)
                    break;
                continue;
            }
            if (sector[0] == 1 && volumeId is null)
            {
                volumeId = Encoding.ASCII.GetString(sector, 40, 32).TrimEnd(' ', '\0');
                volumeSectors = BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(80));
                var root = sector.AsSpan(156, 34);
                rootLba = BinaryPrimitives.ReadUInt32LittleEndian(root[2..]);
                rootSize = BinaryPrimitives.ReadUInt32LittleEndian(root[10..]);
            }
        }

        if (volumeId is null)
            throw new InvalidDataException("No ISO9660 primary volume descriptor found.");
        return new Iso9660(stream, volumeId, volumeSectors, rootLba, rootSize, hasUdf);
    }

    /// <summary>Reads a file from the root directory by name (case-insensitive, ";1" version ignored).</summary>
    /// <returns>File bytes, or null if absent.</returns>
    public byte[]? ReadRootFile(string name)
    {
        var sector = new byte[SectorSize];
        var sectors = (_rootSize + SectorSize - 1) / SectorSize;
        for (uint s = 0; s < sectors; s++)
        {
            if (!TryReadSector(_stream, _rootLba + s, sector))
                throw new InvalidDataException("Root directory extends past end of image.");

            // Records never cross sector boundaries; a zero length byte pads to the next sector.
            for (var pos = 0; pos < SectorSize && sector[pos] != 0;)
            {
                var record = sector.AsSpan(pos, Math.Min(sector[pos], SectorSize - pos));
                pos += sector[pos];
                if (record.Length < 34)
                    break;
                var nameLength = record[32];
                if (33 + nameLength > record.Length)
                    break;
                var isDirectory = (record[25] & 0x02) != 0;
                var identifier = Encoding.ASCII.GetString(record.Slice(33, nameLength));
                var versionMark = identifier.IndexOf(';');
                if (versionMark >= 0)
                    identifier = identifier[..versionMark];
                if (isDirectory || !identifier.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var lba = BinaryPrimitives.ReadUInt32LittleEndian(record[2..]);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(record[10..]);
                if (size > MaxRootFileSize)
                    throw new InvalidDataException($"{name} is too large ({size} bytes).");
                var data = new byte[size];
                _stream.Position = (long)lba * SectorSize;
                if (_stream.ReadAtLeast(data, data.Length, throwOnEndOfStream: false) != data.Length)
                    throw new InvalidDataException($"{name} extends past end of image.");
                return data;
            }
        }
        return null;
    }

    private static bool TryReadSector(Stream stream, long lba, byte[] buffer)
    {
        var offset = lba * SectorSize;
        if (offset + SectorSize > stream.Length)
            return false;
        stream.Position = offset;
        return stream.ReadAtLeast(buffer, SectorSize, throwOnEndOfStream: false) == SectorSize;
    }
}
