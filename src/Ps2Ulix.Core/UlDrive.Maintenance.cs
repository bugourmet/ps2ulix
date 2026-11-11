namespace Ps2Ulix.Core;

public enum IssueSeverity
{
    Warning,
    Error,
}

public sealed record DriveIssue(IssueSeverity Severity, string Message);

public sealed record CheckReport(IReadOnlyList<UlGameEntry> Games, IReadOnlyList<DriveIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
}

/// <param name="Games">New ul.cfg contents.</param>
/// <param name="Recovered">Games found only as part files; named after their game ID, parts renamed to match.</param>
/// <param name="Dropped">ul.cfg entries with no part files on disk.</param>
/// <param name="Incomplete">Part sets with gaps; left on disk, not listed in ul.cfg.</param>
public sealed record RebuildResult(
    IReadOnlyList<UlGameEntry> Games,
    IReadOnlyList<UlGameEntry> Recovered,
    IReadOnlyList<UlGameEntry> Dropped,
    IReadOnlyList<string> Incomplete);

public sealed partial class UlDrive
{
    public const string BackupFileName = UlCfg.FileName + ".bak";

    private readonly record struct PartKey(uint Crc, string Startup)
    {
        public static PartKey Of(UlGameEntry game) => new(game.NameCrc, game.Startup.ToUpperInvariant());
        public override string ToString() => $"ul.{Crc:X8}.{Startup}";
    }

    /// <summary>Part files on disk grouped by (name CRC, game ID); part index → file.</summary>
    private sealed record PartScan(Dictionary<PartKey, SortedDictionary<int, FileInfo>> Groups, List<string> Unrecognized);

    /// <summary>Checks ul.cfg against the part files on disk. Read-only.</summary>
    public CheckReport Check()
    {
        var issues = new List<DriveIssue>();
        void Error(string message) => issues.Add(new DriveIssue(IssueSeverity.Error, message));
        void Warn(string message) => issues.Add(new DriveIssue(IssueSeverity.Warning, message));

        var scan = ScanParts();
        foreach (var name in scan.Unrecognized)
            Warn($"Unrecognized file {name}.");
        var cfg = ScanCfg();
        if (cfg is null)
        {
            if (scan.Groups.Count > 0)
                Error("No ul.cfg but part files exist; run rebuild.");
            return new CheckReport([], issues);
        }

        foreach (var bad in cfg.Records.Where(r => r.Entry is null))
            Error($"ul.cfg record #{bad.Index + 1} is invalid: {bad.Error}");
        if (cfg.TrailingBytes > 0)
            Warn($"ul.cfg has {cfg.TrailingBytes} stray bytes after the last record.");

        var games = cfg.Records.Where(r => r.Entry is not null).Select(r => r.Entry!).ToList();
        foreach (var dup in games.GroupBy(PartKey.Of).Where(g => g.Count() > 1))
            Error($"'{dup.First().Name}' ({dup.Key.Startup}) is listed {dup.Count()} times.");

        foreach (var game in games.DistinctBy(PartKey.Of))
        {
            var label = $"'{game.Name}' ({game.Startup})";
            if (game.Media is not (UlMedia.Cd or UlMedia.Dvd))
                Warn($"{label}: unknown media byte 0x{(byte)game.Media:X2}.");
            if (game.Parts == 0)
            {
                Error($"{label}: part count is 0.");
                continue;
            }

            var files = scan.Groups.GetValueOrDefault(PartKey.Of(game)) ?? [];
            scan.Groups.Remove(PartKey.Of(game));
            var missing = Enumerable.Range(0, game.Parts).Where(p => !files.ContainsKey(p)).ToList();
            if (missing.Count > 0)
            {
                Error($"{label}: missing part(s) {string.Join(", ", missing.Select(p => p.ToString("x2")))}.");
                continue;
            }
            foreach (var extra in files.Keys.Where(p => p >= game.Parts))
                Warn($"{label}: extra part file {files[extra].Name} beyond part count {game.Parts}.");

            for (var p = 0; p < game.Parts - 1; p++)
            {
                if (files[p].Length != PartSize)
                    Error($"{label}: part {p:x2} is {files[p].Length:N0} bytes; OPL expects {PartSize:N0}.");
            }
            var last = files[game.Parts - 1].Length;
            if (last == 0 || last > PartSize)
                Error($"{label}: last part is {last:N0} bytes.");
            var total = files.Where(f => f.Key < game.Parts).Sum(f => f.Value.Length);
            if (total % Iso9660.SectorSize != 0)
                Warn($"{label}: size {total:N0} is not a whole number of 2048-byte sectors.");

            try
            {
                using var iso = OpenGameImage(game);
                var info = Ps2Iso.Read(iso);
                if (!info.GameId.Equals(game.Startup, StringComparison.OrdinalIgnoreCase))
                    Warn($"{label}: disc boots {info.GameId}, ul.cfg says {game.Startup}.");
            }
            catch (InvalidDataException ex)
            {
                Warn($"{label}: not readable as a PS2 ISO: {ex.Message}");
            }
        }

        foreach (var (key, files) in scan.Groups)
            Warn($"{files.Count} orphan part file(s) {key}.* not in ul.cfg; run rebuild to recover.");

        return new CheckReport(games, issues);
    }

    /// <summary>
    /// Rebuilds ul.cfg from the part files on disk. Keeps names/media of games already listed;
    /// recovers unlisted complete part sets (name = game ID, parts renamed to its CRC).
    /// Old ul.cfg is copied to ul.cfg.bak first.
    /// </summary>
    /// <param name="dryRun">Only report; change nothing.</param>
    public RebuildResult Rebuild(bool dryRun = false)
    {
        var scan = ScanParts();
        var listed = (ScanCfg()?.Records ?? []).Where(r => r.Entry is not null).Select(r => r.Entry!)
            .DistinctBy(PartKey.Of).ToList();

        var games = new List<UlGameEntry>();
        var dropped = new List<UlGameEntry>();
        var incomplete = new List<string>();
        foreach (var game in listed)
        {
            if (!scan.Groups.Remove(PartKey.Of(game), out var files))
            {
                dropped.Add(game);
                continue;
            }
            if (!IsComplete(files))
            {
                incomplete.Add($"'{game.Name}' ({game.Startup}): {Describe(PartKey.Of(game), files)}");
                continue;
            }
            games.Add(new UlGameEntry(game.Name, game.Startup, (byte)files.Count, game.Media));
        }

        var recovered = new List<UlGameEntry>();
        var moves = new List<(string From, string To)>();
        var taken = new HashSet<string>(games.SelectMany(g => g.GetPartFileNames()), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, files) in scan.Groups.OrderBy(g => g.Key.Startup, StringComparer.Ordinal))
        {
            if (!IsComplete(files) || files.Count > byte.MaxValue)
            {
                incomplete.Add(Describe(key, files));
                continue;
            }
            // Original startup spelling from the file name; the hashed name is lost, so only the CRC is known.
            UlGameEntry.TryParsePartFileName(files[0].Name, out _, out var startup, out _);
            var media = DetectMedia(files.Values);
            var own = files.Values.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool IsFree(string file) => !taken.Contains(file) && (own.Contains(file) || !File.Exists(Path.Combine(Root, file)));
            var name = startup;
            UlGameEntry game;
            for (var n = 2; ; n++)
            {
                game = new UlGameEntry(name, startup, (byte)files.Count, media);
                if (games.All(g => g.Name != name) && game.GetPartFileNames().All(IsFree))
                    break;
                name = $"{startup} ({n})";
            }
            taken.UnionWith(game.GetPartFileNames());
            games.Add(game);
            recovered.Add(game);
            moves.AddRange(files
                .Select(f => (From: f.Value.FullName, To: GetPartPath(game, f.Key)))
                .Where(m => !m.From.Equals(m.To, StringComparison.OrdinalIgnoreCase)));
        }

        if (!dryRun)
        {
            if (File.Exists(CfgPath))
                File.Copy(CfgPath, Path.Combine(Root, BackupFileName), overwrite: true);
            MoveFilesThenSave(moves, games);
        }
        return new RebuildResult(games, recovered, dropped, incomplete);
    }

    private UlCfgScan? ScanCfg()
    {
        if (!File.Exists(CfgPath))
            return null;
        using var stream = File.OpenRead(CfgPath);
        return UlCfg.Scan(stream);
    }

    private PartScan ScanParts()
    {
        var groups = new Dictionary<PartKey, SortedDictionary<int, FileInfo>>();
        var unrecognized = new List<string>();
        foreach (var file in new DirectoryInfo(Root).EnumerateFiles("ul.*"))
        {
            if (file.Name.Equals(UlCfg.FileName, StringComparison.OrdinalIgnoreCase)
                || file.Name.Equals(UlCfg.FileName + ".new", StringComparison.OrdinalIgnoreCase)
                || file.Name.Equals(BackupFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!UlGameEntry.TryParsePartFileName(file.Name, out var crc, out var startup, out var part))
            {
                unrecognized.Add(file.Name);
                continue;
            }
            var key = new PartKey(crc, startup.ToUpperInvariant());
            if (!groups.TryGetValue(key, out var parts))
                groups[key] = parts = [];
            parts[part] = file;
        }
        return new PartScan(groups, unrecognized);
    }

    /// <summary>Opens a UL game's parts as one read-only ISO stream.</summary>
    public MultiFileStream OpenGameImage(UlGameEntry game) =>
        new(Enumerable.Range(0, game.Parts).Select(p => GetPartPath(game, p)));

    private static bool IsComplete(SortedDictionary<int, FileInfo> files) =>
        files.Count > 0 && files.Keys.Last() == files.Count - 1;

    private static string Describe(PartKey key, SortedDictionary<int, FileInfo> files)
    {
        var max = files.Keys.Last();
        var missing = Enumerable.Range(0, max + 1).Where(p => !files.ContainsKey(p)).Select(p => p.ToString("x2"));
        return $"{key}.*: has {files.Count} part(s), missing {string.Join(", ", missing)}";
    }

    /// <summary>Media of an unlisted part set: from its ISO if readable, else by size.</summary>
    private static UlMedia DetectMedia(IEnumerable<FileInfo> parts)
    {
        try
        {
            using var stream = new MultiFileStream(parts.Select(f => f.FullName));
            return Ps2Iso.Read(stream).Media;
        }
        catch (InvalidDataException)
        {
            return parts.Sum(f => f.Length) > Ps2Iso.MaxCdSectors * Iso9660.SectorSize ? UlMedia.Dvd : UlMedia.Cd;
        }
    }
}
