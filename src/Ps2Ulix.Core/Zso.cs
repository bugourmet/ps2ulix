using System.Buffers.Binary;
using K4os.Compression.LZ4;

namespace Ps2Ulix.Core;

/// <summary>
/// ZSO (ZISO) compressed ISO as read by OPL (<c>modules/isofs/zso.c</c>) and written by its <c>pc/ziso.py</c>:
/// 24-byte header, u32 index (high bit = stored raw, offsets &gt;&gt; align), LZ4 raw blocks of 2048 bytes.
/// </summary>
public static class Zso
{
    public const uint Magic = 0x4F53495A; // "ZISO"
    public const int HeaderSize = 0x18;
    public const int BlockSize = Iso9660.SectorSize; // OPL hardcodes 2048
    public const uint PlainFlag = 0x80000000;

    /// <summary>ziso.py stores a block raw when compression saves less than 5%.</summary>
    private const int CompressThresholdPercent = 95;
    private const byte PaddingByte = (byte)'X';

    /// <summary>Blocks compressed in parallel per batch (2 MiB of ISO).</summary>
    private const int BatchBlocks = 1024;

    public static bool IsZso(Stream stream)
    {
        if (stream.Length < HeaderSize)
            return false;
        Span<byte> magic = stackalloc byte[4];
        stream.Position = 0;
        stream.ReadExactly(magic);
        stream.Position = 0;
        return BinaryPrimitives.ReadUInt32LittleEndian(magic) == Magic;
    }

    /// <summary>Compresses a 2048-byte-sector ISO. Output must be seekable (index is written last).</summary>
    /// <param name="progress">Input bytes processed.</param>
    public static void Compress(Stream iso, Stream zso, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!zso.CanSeek)
            throw new ArgumentException("Output must be seekable.", nameof(zso));
        var total = iso.Length - iso.Position;
        if (total % BlockSize != 0)
            throw new InvalidDataException("ISO size is not a whole number of 2048-byte sectors.");
        var blocks = total / BlockSize;
        // Same rule as ziso.py: keep shifted offsets below 2^31 so the high bit stays free for the raw flag.
        var align = (int)(total >> 31);

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), HeaderSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), (ulong)total);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), BlockSize);
        header[20] = 1; // version
        header[21] = (byte)align;

        var start = zso.Position;
        var index = new uint[blocks + 1];
        zso.Write(header);
        zso.Write(new byte[index.Length * 4]);

        // Blocks are independent, so a batch is compressed on all cores, then written in order.
        var plain = new byte[BatchBlocks * BlockSize];
        var packed = new byte[BatchBlocks][];
        var lengths = new int[BatchBlocks];
        for (var i = 0; i < BatchBlocks; i++)
            packed[i] = new byte[LZ4Codec.MaximumOutputSize(BlockSize)];
        var pad = new byte[1 << align];
        pad.AsSpan().Fill(PaddingByte);
        var parallel = new ParallelOptions { CancellationToken = cancellationToken };
        for (long first = 0; first < blocks; first += BatchBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(BatchBlocks, blocks - first);
            iso.ReadExactly(plain, 0, count * BlockSize);
            Parallel.For(0, count, parallel, i =>
                lengths[i] = LZ4Codec.Encode(plain.AsSpan(i * BlockSize, BlockSize), packed[i], LZ4Level.L09_HC));

            for (var i = 0; i < count; i++)
            {
                var b = first + i;
                var offset = zso.Position - start;
                var misalign = (int)(offset & ((1 << align) - 1));
                if (misalign != 0)
                {
                    zso.Write(pad, 0, pad.Length - misalign);
                    offset += pad.Length - misalign;
                }

                index[b] = checked((uint)(offset >> align));
                var length = lengths[i];
                if (length <= 0 || 100L * length / BlockSize >= CompressThresholdPercent)
                {
                    index[b] |= PlainFlag;
                    zso.Write(plain, i * BlockSize, BlockSize);
                }
                else
                {
                    if ((index[b] & PlainFlag) != 0)
                        throw new InvalidOperationException("ZSO offset overflow; align too small.");
                    zso.Write(packed[i], 0, length);
                }
            }
            progress?.Report((first + count) * BlockSize);
        }
        index[blocks] = checked((uint)((zso.Position - start) >> align));

        var end = zso.Position;
        var indexBytes = new byte[index.Length * 4];
        for (var i = 0; i < index.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(indexBytes.AsSpan(i * 4), index[i]);
        zso.Position = start + HeaderSize;
        zso.Write(indexBytes);
        zso.Position = end;
    }

    /// <summary>Expands a ZSO back to a plain ISO.</summary>
    /// <param name="progress">Output bytes written.</param>
    public static void Decompress(Stream zso, Stream iso, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        using var reader = new ZsoReader(zso, leaveOpen: true);
        FastCopy.Copy(reader, iso, reader.Length, progress, cancellationToken: cancellationToken);
    }
}

/// <summary>Read-only seekable view of a ZSO as the plain ISO it holds.</summary>
public sealed class ZsoReader : Stream
{
    private readonly Stream _zso;
    private readonly bool _leaveOpen;
    private readonly int _align;
    private readonly uint[] _index;
    private readonly byte[] _block = new byte[Zso.BlockSize];
    private readonly byte[] _packed = new byte[2 * Zso.BlockSize];
    private long _cachedBlock = -1;
    private long _position;

    /// <exception cref="InvalidDataException">Not a ZSO, or block size other than 2048.</exception>
    public ZsoReader(Stream zso, bool leaveOpen = false)
    {
        _zso = zso;
        _leaveOpen = leaveOpen;
        var header = new byte[Zso.HeaderSize];
        zso.Position = 0;
        if (zso.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length
            || BinaryPrimitives.ReadUInt32LittleEndian(header) != Zso.Magic)
            throw new InvalidDataException("Not a ZSO file.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16)) != Zso.BlockSize)
            throw new InvalidDataException("ZSO block size must be 2048 (OPL only reads that).");
        Length = (long)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8));
        _align = header[21];

        var blocks = Length / Zso.BlockSize;
        var indexBytes = new byte[(blocks + 1) * 4];
        zso.Position = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        zso.ReadExactly(indexBytes);
        _index = new uint[blocks + 1];
        for (var i = 0; i < _index.Length; i++)
            _index[i] = BinaryPrimitives.ReadUInt32LittleEndian(indexBytes.AsSpan(i * 4));
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var total = 0;
        while (buffer.Length > 0 && _position < Length)
        {
            var block = _position / Zso.BlockSize;
            LoadBlock(block);
            var inBlock = (int)(_position % Zso.BlockSize);
            var n = (int)Math.Min(Math.Min(buffer.Length, Zso.BlockSize - inBlock), Length - _position);
            _block.AsSpan(inBlock, n).CopyTo(buffer);
            buffer = buffer[n..];
            _position += n;
            total += n;
        }
        return total;
    }

    private void LoadBlock(long block)
    {
        if (block == _cachedBlock)
            return;
        var entry = _index[block];
        var start = (long)(entry & ~Zso.PlainFlag) << _align;
        var end = (long)(_index[block + 1] & ~Zso.PlainFlag) << _align;
        // Span may include alignment padding before the next block; decoders stop at 2048 output bytes.
        var size = (int)Math.Min(end - start, _packed.Length);
        _zso.Position = start;
        _zso.ReadExactly(_packed, 0, size);
        if ((entry & Zso.PlainFlag) != 0)
        {
            if (size < Zso.BlockSize)
                throw new InvalidDataException($"ZSO block {block} is truncated.");
            _packed.AsSpan(0, Zso.BlockSize).CopyTo(_block);
        }
        else if (LZ4Codec.PartialDecode(_packed.AsSpan(0, size), _block) != Zso.BlockSize)
        {
            throw new InvalidDataException($"ZSO block {block} is corrupt.");
        }
        _cachedBlock = block;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        SeekOrigin.End => Length + offset,
        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    };

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
            _zso.Dispose();
        base.Dispose(disposing);
    }
}
