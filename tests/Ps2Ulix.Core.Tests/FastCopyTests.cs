namespace Ps2Ulix.Core.Tests;

public class FastCopyTests
{
    private static byte[] Data(int size)
    {
        var data = new byte[size];
        new Random(size).NextBytes(data);
        return data;
    }

    [Fact]
    public void Copy_All_SpansSeveralChunks()
    {
        var data = Data(FastCopy.ChunkSize * 2 + 12345);
        using var target = new MemoryStream();

        var copied = FastCopy.Copy(new MemoryStream(data), target);

        Assert.Equal(data.Length, copied);
        Assert.Equal(data, target.ToArray());
    }

    [Fact]
    public void Copy_Count_StopsThereAndLeavesRest()
    {
        var data = Data(FastCopy.ChunkSize + 100);
        var source = new MemoryStream(data);
        using var target = new MemoryStream();

        FastCopy.Copy(source, target, count: 5000);

        Assert.Equal(data[..5000], target.ToArray());
        Assert.Equal(5000, source.Position);
    }

    [Fact]
    public void Copy_SourceTooShort_Throws()
    {
        Assert.Throws<EndOfStreamException>(() => FastCopy.Copy(new MemoryStream(Data(100)), new MemoryStream(), count: 200));
    }

    [Fact]
    public void Copy_ReportsThrottledProgressAndFinalTotal()
    {
        var data = Data((int)(FastCopy.ReportEvery * 3));
        var reports = new List<long>();

        FastCopy.Copy(new MemoryStream(data), new MemoryStream(), progress: new SyncProgress(reports.Add), doneBefore: 10);

        Assert.InRange(reports.Count, 2, 5);
        Assert.Equal(data.Length + 10, reports[^1]);
    }

    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
