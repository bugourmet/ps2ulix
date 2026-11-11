using System.Globalization;
using System.Text;

namespace Ps2Ulix.Core;

/// <summary>A folder (usually a USB drive root) holding UL-format games: ul.cfg plus ul.* part files.</summary>
public sealed partial class UlDrive
{
    /// <summary>USBUtil/OPL part size: 1 GiB, under the FAT32 4 GiB file limit.</summary>
    public const long DefaultPartSize = 1L << 30;

    public UlDrive(string root, long partSize = DefaultPartSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(partSize, Iso9660.SectorSize);
        Root = Path.GetFullPath(root);
        PartSize = partSize;
        if (!Directory.Exists(Root))
            throw new DirectoryNotFoundException($"Not found: {Root}");
    }

    public string Root { get; }

    public long PartSize { get; }

    public string CfgPath => Path.Combine(Root, UlCfg.FileName);

    public List<UlGameEntry> LoadGames() => UlCfg.Load(CfgPath);

    public string GetPartPath(UlGameEntry game, int part) => Path.Combine(Root, game.GetPartFileName(part));

    /// <summary>Finds a game by exact name or game ID (case-insensitive).</summary>
    /// <exception cref="InvalidOperationException">No match, or several.</exception>
    public UlGameEntry FindGame(string nameOrId)
    {
        var matches = LoadGames()
            .Where(g => g.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)
                     || g.Startup.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No game '{nameOrId}'."),
            _ => throw new InvalidOperationException($"'{nameOrId}' matches {matches.Count} games; use the exact name."),
        };
    }

    /// <summary>Splits an ISO into parts on this drive and adds it to ul.cfg.</summary>
    /// <param name="name">Game name; default from ISO file name.</param>
    /// <param name="media">Overrides CD/DVD detection.</param>
    /// <param name="progress">Bytes written so far.</param>
    public UlGameEntry AddGame(string isoPath, string? name = null, UlMedia? media = null,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var info = Ps2Iso.Read(isoPath);
        var partCount = Math.Max(1, (info.Size + PartSize - 1) / PartSize);
        if (partCount > byte.MaxValue)
            throw new InvalidOperationException($"ISO needs {partCount} parts; max is {byte.MaxValue}.");

        var game = new UlGameEntry(name ?? NameFromFile(isoPath), info.GameId, (byte)partCount, media ?? info.Media);
        var games = LoadGames();
        EnsureNoClash(games, game);
        EnsureFreeSpace(info.Size);

        var written = new List<string>();
        try
        {
            using var source = Ps2Iso.OpenImage(isoPath);
            long done = 0;
            for (var part = 0; part < partCount; part++)
            {
                var path = GetPartPath(game, part);
                using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                written.Add(path);
                done += FastCopy.Copy(source, target, Math.Min(PartSize, info.Size - done), progress, done, cancellationToken);
            }
            games.Add(game);
            UlCfg.Save(CfgPath, games);
            return game;
        }
        catch
        {
            foreach (var path in written)
                TryDelete(path);
            throw;
        }
    }

    /// <summary>Concatenates a game's parts back into an ISO.</summary>
    public void JoinGame(UlGameEntry game, string outputIsoPath,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var parts = Enumerable.Range(0, game.Parts).Select(p => GetPartPath(game, p)).ToList();
        var missing = parts.Where(p => !File.Exists(p)).ToList();
        if (missing.Count > 0)
            throw new FileNotFoundException($"Missing part(s): {string.Join(", ", missing.Select(Path.GetFileName))}");
        if (File.Exists(outputIsoPath))
            throw new IOException($"Output already exists: {outputIsoPath}");

        try
        {
            using var target = new FileStream(outputIsoPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            long done = 0;
            foreach (var part in parts)
            {
                using var source = File.OpenRead(part);
                done += FastCopy.Copy(source, target, source.Length, progress, done, cancellationToken);
            }
        }
        catch
        {
            TryDelete(outputIsoPath);
            throw;
        }
    }

    /// <summary>Removes a game from ul.cfg, then deletes its parts. Missing parts are ignored.</summary>
    public void DeleteGame(UlGameEntry game)
    {
        var games = LoadGames();
        if (!games.Remove(game))
            throw new InvalidOperationException($"'{game.Name}' is not in ul.cfg.");
        UlCfg.Save(CfgPath, games);
        for (var part = 0; part < game.Parts; part++)
            TryDelete(GetPartPath(game, part));
    }

    /// <summary>Renames a game. Part files carry the name's CRC, so they are renamed too; rolled back on failure.</summary>
    public UlGameEntry RenameGame(UlGameEntry game, string newName)
    {
        var games = LoadGames();
        var index = games.IndexOf(game);
        if (index < 0)
            throw new InvalidOperationException($"'{game.Name}' is not in ul.cfg.");
        var renamed = new UlGameEntry(newName, game.Startup, game.Parts, game.Media);
        if (renamed.NameCrc == game.NameCrc)
        {
            games[index] = renamed;
            UlCfg.Save(CfgPath, games);
            return renamed;
        }
        EnsureNoClash(games.Where((_, i) => i != index), renamed);

        games[index] = renamed;
        MoveFilesThenSave(Enumerable.Range(0, game.Parts).Select(p => (GetPartPath(game, p), GetPartPath(renamed, p))), games);
        return renamed;
    }

    /// <summary>Renames part files, then saves ul.cfg; undoes the moves on failure.</summary>
    private void MoveFilesThenSave(IEnumerable<(string From, string To)> moves, List<UlGameEntry> games)
    {
        var moved = new List<(string From, string To)>();
        try
        {
            foreach (var (from, to) in moves)
            {
                File.Move(from, to);
                moved.Add((from, to));
            }
            UlCfg.Save(CfgPath, games);
        }
        catch
        {
            foreach (var (from, to) in Enumerable.Reverse(moved))
                File.Move(to, from);
            throw;
        }
    }

    /// <summary>Default game name from an ISO file name: drops OPL-style "SLUS_123.45." prefix, folds to ASCII, max 32 chars.</summary>
    public static string NameFromFile(string isoPath)
    {
        var (_, name) = IsoFolders.SplitOldStyleName(Path.GetFileNameWithoutExtension(isoPath));

        var ascii = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            ascii.Append(c is >= ' ' and <= '~' ? c : '_');
        }
        var result = ascii.ToString().Trim();
        if (result.Length > UlGameEntry.NameLength)
            result = result[..UlGameEntry.NameLength].TrimEnd();
        return result.Length == 0 ? "Game" : result;
    }

    private void EnsureNoClash(IEnumerable<UlGameEntry> games, UlGameEntry game)
    {
        if (games.Any(g => g.NameCrc == game.NameCrc && g.Startup == game.Startup))
            throw new InvalidOperationException($"A game with the same name and ID ({game.Startup}) already exists.");
        var existing = Enumerable.Range(0, game.Parts).Select(p => GetPartPath(game, p)).FirstOrDefault(File.Exists);
        if (existing is not null)
            throw new IOException($"Part file already exists: {Path.GetFileName(existing)}");
    }

    private void EnsureFreeSpace(long needed)
    {
        long free;
        try
        {
            free = new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return; // Unknown (e.g. UNC path); let the write fail if full.
        }
        if (free < needed)
            throw new IOException($"Not enough space: need {needed:N0} bytes, {free:N0} free.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
