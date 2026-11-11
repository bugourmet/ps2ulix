namespace Ps2Ulix.Core.Tests;

/// <summary>Temp work dir with a "usb" drive folder using tiny parts, plus synthetic ISO helpers.</summary>
public abstract class DriveTestBase : IDisposable
{
    protected const long PartSize = 4 * Iso9660.SectorSize;

    protected readonly string _work = Directory.CreateTempSubdirectory("ps2ulix-drive-").FullName;
    protected readonly string _driveDir;
    protected readonly UlDrive _drive;

    protected DriveTestBase()
    {
        _driveDir = Directory.CreateDirectory(Path.Combine(_work, "usb")).FullName;
        _drive = new UlDrive(_driveDir, PartSize);
    }

    public void Dispose() => Directory.Delete(_work, recursive: true);

    protected string MakeIso(string fileName, string gameId = "SLUS_209.46", int payload = 20_000)
    {
        var data = new byte[payload];
        new Random(payload).NextBytes(data);
        using var iso = new TestIsoBuilder()
            .AddFile("SYSTEM.CNF;1", $"BOOT2 = cdrom0:\\{gameId};1\r\n")
            .AddFile("DATA.BIN;1", data)
            .Build();
        var path = Path.Combine(_work, fileName);
        File.WriteAllBytes(path, iso.ToArray());
        return path;
    }

    /// <summary>Part file names on the drive (ul.cfg and backups excluded), sorted.</summary>
    protected string[] PartFiles() => Directory.GetFiles(_driveDir, "ul.*.*")
        .Select(f => Path.GetFileName(f)!)
        .Where(f => !f.StartsWith(UlCfg.FileName, StringComparison.OrdinalIgnoreCase))
        .Order().ToArray();

    /// <summary>Progress&lt;T&gt; posts to a sync context/thread pool; tests need synchronous callbacks.</summary>
    protected sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
