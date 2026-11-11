namespace Ps2Ulix.Core.Tests;

public sealed class MultiFileStreamTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ps2ulix-mfs-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string[] Write(params byte[][] parts) => parts.Select((data, i) =>
    {
        var path = Path.Combine(_dir, $"p{i}");
        File.WriteAllBytes(path, data);
        return path;
    }).ToArray();

    [Fact]
    public void Read_SpansFilesAndSkipsEmptyOnes()
    {
        using var stream = new MultiFileStream(Write([1, 2, 3], [], [4, 5], [6]));

        Assert.Equal(6, stream.Length);
        var all = new byte[10];
        Assert.Equal(6, stream.ReadAtLeast(all, 10, throwOnEndOfStream: false));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, all[..6]);
    }

    [Fact]
    public void Seek_ReadsFromAnyOffset()
    {
        using var stream = new MultiFileStream(Write([1, 2, 3], [4, 5], [6]));
        stream.Seek(-4, SeekOrigin.End);

        var buffer = new byte[3];
        stream.ReadExactly(buffer);

        Assert.Equal(new byte[] { 3, 4, 5 }, buffer);
        Assert.Equal(5, stream.Position);
    }
}
