namespace Ps2Ulix.Core;

/// <summary>
/// Stream copy for big images: reads the next chunk while the previous one is written, so a USB
/// write and a disk read overlap instead of taking turns. Progress is reported at most every
/// <see cref="ReportEvery"/> bytes, so the UI is not flooded.
/// </summary>
public static class FastCopy
{
    public const int ChunkSize = 4 << 20;
    public const long ReportEvery = 8 << 20;

    /// <summary>Copies exactly <paramref name="count"/> bytes, or everything when null.</summary>
    /// <param name="doneBefore">Added to reported values, for jobs made of several copies.</param>
    /// <returns>Bytes copied.</returns>
    /// <exception cref="EndOfStreamException">Source ended before <paramref name="count"/> bytes.</exception>
    public static long Copy(Stream source, Stream target, long? count = null, IProgress<long>? progress = null,
        long doneBefore = 0, CancellationToken cancellationToken = default)
    {
        var buffers = new[] { new byte[ChunkSize], new byte[ChunkSize] };
        long copied = 0, lastReport = 0;
        var current = 0;
        var pending = ReadNext(source, buffers[current], count, copied, cancellationToken);
        while (true)
        {
            var read = pending.GetAwaiter().GetResult();
            if (read == 0)
                break;
            copied += read;
            // Start reading the next chunk before this one is written.
            pending = ReadNext(source, buffers[1 - current], count, copied, cancellationToken);
            try
            {
                target.Write(buffers[current], 0, read);
            }
            catch
            {
                pending.ContinueWith(_ => { }, TaskScheduler.Default).Wait(cancellationToken);
                throw;
            }
            current = 1 - current;
            if (copied - lastReport >= ReportEvery)
            {
                lastReport = copied;
                progress?.Report(doneBefore + copied);
            }
        }
        if (count is { } expected && copied < expected)
            throw new EndOfStreamException("Source ended early.");
        progress?.Report(doneBefore + copied);
        return copied;
    }

    private static Task<int> ReadNext(Stream source, byte[] buffer, long? count, long copied, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var want = count is { } total ? (int)Math.Min(buffer.Length, total - copied) : buffer.Length;
        if (want <= 0)
            return Task.FromResult(0);
        return Task.Run(() => source.ReadAtLeast(buffer.AsSpan(0, want), want, throwOnEndOfStream: false), cancellationToken);
    }
}
