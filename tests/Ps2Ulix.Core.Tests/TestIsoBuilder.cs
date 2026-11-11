using System.Buffers.Binary;
using System.Text;

namespace Ps2Ulix.Core.Tests;

/// <summary>Builds tiny in-memory ISO9660 images: PVD, optional UDF bridge descriptors, root-dir files.</summary>
internal sealed class TestIsoBuilder
{
    private const int Sector = Iso9660.SectorSize;
    private readonly List<(string Name, byte[] Data, bool IsDirectory)> _entries = [];

    public string VolumeId { get; init; } = "TEST_DISC";
    public bool Udf { get; init; }

    /// <summary>Overrides the PVD volume size field; default is the real image size.</summary>
    public uint? VolumeSectors { get; init; }

    public TestIsoBuilder AddFile(string name, string text) => AddFile(name, Encoding.ASCII.GetBytes(text));

    public TestIsoBuilder AddFile(string name, byte[] data)
    {
        _entries.Add((name, data, false));
        return this;
    }

    public TestIsoBuilder AddDirectory(string name)
    {
        _entries.Add((name, [], true));
        return this;
    }

    public MemoryStream Build()
    {
        var descriptors = new List<byte[]> { new byte[Sector], Descriptor(255, "CD001") };
        if (Udf)
            descriptors.AddRange([Descriptor(0, "BEA01"), Descriptor(0, "NSR02"), Descriptor(0, "TEA01")]);
        var rootLba = (uint)(16 + descriptors.Count);

        // Lay out records first to know how many sectors the root dir takes.
        var records = new List<(int Sector, int Offset, int Index)>();
        int sector = 0, offset = 0;
        var all = new List<(string Name, byte[] Data, bool IsDirectory)> { ("\0", [], true), ("\u0001", [], true) };
        all.AddRange(_entries);
        for (var i = 0; i < all.Count; i++)
        {
            var len = RecordLength(all[i].Name);
            if (offset + len > Sector)
            {
                sector++;
                offset = 0;
            }
            records.Add((sector, offset, i));
            offset += len;
        }
        var rootSectors = sector + 1;

        var nextLba = rootLba + (uint)rootSectors;
        var dataLbas = new uint[all.Count];
        for (var i = 0; i < all.Count; i++)
        {
            dataLbas[i] = all[i].IsDirectory ? rootLba : nextLba;
            nextLba += (uint)((all[i].Data.Length + Sector - 1) / Sector);
        }

        var image = new byte[nextLba * Sector];
        var pvd = Descriptor(1, "CD001");
        Encoding.ASCII.GetBytes(VolumeId.PadRight(32)).CopyTo(pvd, 40);
        BinaryPrimitives.WriteUInt32LittleEndian(pvd.AsSpan(80), VolumeSectors ?? nextLba);
        BinaryPrimitives.WriteUInt16LittleEndian(pvd.AsSpan(128), Sector);
        WriteRecord(pvd.AsSpan(156), "\0", rootLba, (uint)(rootSectors * Sector), isDirectory: true);
        pvd.CopyTo(image, 16 * Sector);
        for (var i = 1; i < descriptors.Count; i++)
            descriptors[i].CopyTo(image, (16 + i) * Sector);

        foreach (var (sec, off, idx) in records)
        {
            var (name, data, isDir) = all[idx];
            var size = isDir ? (uint)(rootSectors * Sector) : (uint)data.Length;
            WriteRecord(image.AsSpan((int)((rootLba + sec) * Sector) + off), name, dataLbas[idx], size, isDir);
            if (!isDir)
                data.CopyTo(image, dataLbas[idx] * Sector);
        }
        return new MemoryStream(image);
    }

    private static byte[] Descriptor(byte type, string id)
    {
        var d = new byte[Sector];
        d[0] = type;
        Encoding.ASCII.GetBytes(id).CopyTo(d, 1);
        d[6] = 1;
        return d;
    }

    private static int RecordLength(string name) => 33 + name.Length + (name.Length % 2 == 0 ? 1 : 0);

    private static void WriteRecord(Span<byte> dest, string name, uint lba, uint size, bool isDirectory)
    {
        var len = RecordLength(name);
        dest[0] = (byte)len;
        BinaryPrimitives.WriteUInt32LittleEndian(dest[2..], lba);
        BinaryPrimitives.WriteUInt32BigEndian(dest[6..], lba);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[10..], size);
        BinaryPrimitives.WriteUInt32BigEndian(dest[14..], size);
        dest[25] = (byte)(isDirectory ? 0x02 : 0x00);
        dest[32] = (byte)name.Length;
        Encoding.Latin1.GetBytes(name).CopyTo(dest[33..]);
    }
}
