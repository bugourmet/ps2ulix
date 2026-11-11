namespace Ps2Ulix.Core;

/// <summary>One ul.cfg record: parsed entry, or why it could not be parsed.</summary>
public sealed record UlCfgRecord(int Index, UlGameEntry? Entry, string? Error);

/// <param name="TrailingBytes">Bytes after the last full record (OPL ignores them).</param>
public sealed record UlCfgScan(IReadOnlyList<UlCfgRecord> Records, int TrailingBytes);

/// <summary>Reads/writes ul.cfg: a flat array of 64-byte <see cref="UlGameEntry"/> records, no header.</summary>
public static class UlCfg
{
    public const string FileName = "ul.cfg";

    /// <summary>Reads all full records. A trailing partial record is ignored, as OPL does.</summary>
    public static List<UlGameEntry> Read(Stream stream)
    {
        var entries = new List<UlGameEntry>();
        var buffer = new byte[UlGameEntry.Size];
        while (stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length)
            entries.Add(UlGameEntry.Parse(buffer));
        return entries;
    }

    /// <summary>Reads every record without throwing on bad ones; for check/rebuild of damaged files.</summary>
    public static UlCfgScan Scan(Stream stream)
    {
        var records = new List<UlCfgRecord>();
        var buffer = new byte[UlGameEntry.Size];
        int read;
        while ((read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false)) == buffer.Length)
        {
            try
            {
                records.Add(new UlCfgRecord(records.Count, UlGameEntry.Parse(buffer), null));
            }
            catch (ArgumentException ex)
            {
                records.Add(new UlCfgRecord(records.Count, null, ex.Message));
            }
        }
        return new UlCfgScan(records, read);
    }

    public static void Write(Stream stream, IEnumerable<UlGameEntry> entries)
    {
        var buffer = new byte[UlGameEntry.Size];
        foreach (var entry in entries)
        {
            entry.WriteTo(buffer);
            stream.Write(buffer);
        }
    }

    /// <summary>Loads ul.cfg; missing file means no games.</summary>
    public static List<UlGameEntry> Load(string path)
    {
        if (!File.Exists(path))
            return [];
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Writes to <c>ul.cfg.new</c> then replaces <c>ul.cfg</c>, so a crash never leaves a half-written list.</summary>
    public static void Save(string path, IEnumerable<UlGameEntry> entries)
    {
        var tempPath = path + ".new";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Write(stream, entries);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }
}
