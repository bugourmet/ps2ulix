namespace Ps2Ulix.Core;

/// <summary>Read-only seekable stream over files laid end to end (a UL game's parts as one ISO).</summary>
public sealed class MultiFileStream : Stream
{
    private readonly FileStream[] _files;
    private readonly long[] _starts;
    private long _position;

    public MultiFileStream(IEnumerable<string> paths)
    {
        var opened = new List<FileStream>();
        try
        {
            foreach (var path in paths)
                opened.Add(File.OpenRead(path));
        }
        catch
        {
            opened.ForEach(f => f.Dispose());
            throw;
        }
        _files = [.. opened];
        _starts = new long[_files.Length];
        long start = 0;
        for (var i = 0; i < _files.Length; i++)
        {
            _starts[i] = start;
            start += _files[i].Length;
        }
        Length = start;
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
            var i = Array.BinarySearch(_starts, _position);
            if (i < 0)
                i = ~i - 1;
            while (_position >= _starts[i] + _files[i].Length)
                i++; // skip empty files sharing a start offset
            var file = _files[i];
            file.Position = _position - _starts[i];
            var read = file.Read(buffer[..(int)Math.Min(buffer.Length, file.Length - file.Position)]);
            if (read == 0)
                break;
            _position += read;
            total += read;
            buffer = buffer[read..];
        }
        return total;
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
        if (disposing)
        {
            foreach (var f in _files)
                f.Dispose();
        }
        base.Dispose(disposing);
    }
}
