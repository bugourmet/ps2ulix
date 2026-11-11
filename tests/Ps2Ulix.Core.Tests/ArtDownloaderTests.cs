using System.Net;

namespace Ps2Ulix.Core.Tests;

public sealed class ArtDownloaderTests : IDisposable
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];
    private readonly string _root = Directory.CreateTempSubdirectory("ps2ulix-art-").FullName;
    private readonly FakeHandler _handler = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ArtDownloader Downloader => new(new HttpClient(_handler), "https://art.test/PS2/");

    [Fact]
    public async Task Download_SavesPngsAndReportsMissing()
    {
        _handler.Files["/PS2/SLUS_209.46/SLUS_209.46_COV.png"] = Png;

        var results = await Downloader.DownloadAsync(_root, "SLUS_209.46", ["COV", "COV2"]);

        Assert.Equal([ArtOutcome.Downloaded, ArtOutcome.NotFound], results.Select(r => r.Outcome));
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(_root, "ART", "SLUS_209.46_COV.png")));
        Assert.False(File.Exists(Path.Combine(_root, "ART", "SLUS_209.46_COV2.png")));
    }

    [Fact]
    public async Task Download_Existing_SkippedUnlessOverwrite()
    {
        _handler.Files["/PS2/SLUS_209.46/SLUS_209.46_COV.png"] = Png;
        var path = ArtDownloader.GetPath(_root, "SLUS_209.46", "COV");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "old");

        var skipped = await Downloader.DownloadAsync(_root, "SLUS_209.46", ["COV"]);
        Assert.Equal(ArtOutcome.AlreadyExists, skipped.Single().Outcome);
        Assert.Equal(0, _handler.Requests);

        await Downloader.DownloadAsync(_root, "SLUS_209.46", ["COV"], overwrite: true);
        Assert.Equal(Png, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Download_NonPng_ThrowsAndSavesNothing()
    {
        _handler.Files["/PS2/SLUS_209.46/SLUS_209.46_COV.png"] = "<html>"u8.ToArray();

        await Assert.ThrowsAsync<InvalidDataException>(() => Downloader.DownloadAsync(_root, "SLUS_209.46", ["COV"]));
        Assert.False(Directory.Exists(Path.Combine(_root, "ART")));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("SLUS 209")]
    [InlineData("")]
    public async Task Download_BadId_Throws(string id)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Downloader.DownloadAsync(_root, id, ["COV"]));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(Files.TryGetValue(request.RequestUri!.AbsolutePath, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
