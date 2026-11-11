namespace Ps2Ulix.Core;

public enum IsoFormat
{
    Iso,
    Zso,
}

/// <summary>A game stored as a single image in the drive's CD/ or DVD/ folder (OPL "ISO" mode).</summary>
/// <param name="GameId">From an OPL-style "SLUS_123.45." name prefix, else read from the image; null if unreadable.</param>
public sealed record IsoGame(string Path, string Name, string? GameId, UlMedia Media, IsoFormat Format, long FileSize);

/// <summary>OPL ISO mode: <c>CD/&lt;name&gt;.iso|zso</c> and <c>DVD/&lt;name&gt;.iso|zso</c> under the drive root.</summary>
public sealed class IsoFolders
{
    /// <summary>OPL's ISO_GAME_NAME_MAX.</summary>
    public const int MaxNameLength = 160;

    /// <summary>FAT32 file size limit; bigger images need exFAT, UL format or ZSO.</summary>
    public const long Fat32MaxFileSize = (1L << 32) - 1;

    private const string FatInvalidChars = "\"*/:<>?\\|";

    public IsoFolders(string root)
    {
        Root = System.IO.Path.GetFullPath(root);
        if (!Directory.Exists(Root))
            throw new DirectoryNotFoundException($"Not found: {Root}");
    }

    public string Root { get; }

    public string GetFolder(UlMedia media) => System.IO.Path.Combine(Root, media == UlMedia.Cd ? "CD" : "DVD");

    public List<IsoGame> LoadGames()
    {
        var games = new List<IsoGame>();
        foreach (var media in new[] { UlMedia.Cd, UlMedia.Dvd })
        {
            var folder = new DirectoryInfo(GetFolder(media));
            if (!folder.Exists)
                continue;
            foreach (var file in folder.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                var format = file.Extension.ToLowerInvariant() switch
                {
                    ".iso" => IsoFormat.Iso,
                    ".zso" => IsoFormat.Zso,
                    _ => (IsoFormat?)null,
                };
                if (format is null)
                    continue;
                var (id, name) = SplitOldStyleName(System.IO.Path.GetFileNameWithoutExtension(file.Name));
                id ??= TryReadGameId(file.FullName);
                games.Add(new IsoGame(file.FullName, name, id, media, format.Value, file.Length));
            }
        }
        return games;
    }

    /// <summary>Copies (or compresses to ZSO) an image into CD/ or DVD/. Source may be .iso or .zso.</summary>
    public IsoGame AddGame(string imagePath, string? name = null, UlMedia? media = null, IsoFormat format = IsoFormat.Iso,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var info = Ps2Iso.Read(imagePath);
        return AddGame(() => Ps2Iso.OpenImage(imagePath), info, name ?? UlDrive.NameFromFile(imagePath),
            media ?? info.Media, format, progress, cancellationToken);
    }

    /// <summary>Writes a game from any ISO byte source (e.g. a UL game's joined parts).</summary>
    /// <param name="progress">Source bytes processed.</param>
    public IsoGame AddGame(Func<Stream> openSource, Ps2IsoInfo info, string name, UlMedia media, IsoFormat format,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        if (format == IsoFormat.Iso && info.Size > Fat32MaxFileSize && IsFat32())
            throw new IOException("ISO is over 4 GiB, too big for FAT32. Use ZSO, UL format, or an exFAT drive.");
        EnsureFreeSpace(format == IsoFormat.Iso ? info.Size : 0);

        var folder = GetFolder(media);
        Directory.CreateDirectory(folder);
        var target = System.IO.Path.Combine(folder, name + (format == IsoFormat.Iso ? ".iso" : ".zso"));
        if (File.Exists(target))
            throw new IOException($"Already exists: {target}");

        var temp = target + ".part";
        try
        {
            using (var source = openSource())
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                if (format == IsoFormat.Zso)
                    Zso.Compress(source, output, progress, cancellationToken);
                else
                    FastCopy.Copy(source, output, progress: progress, cancellationToken: cancellationToken);
                if (output.Length > Fat32MaxFileSize && IsFat32())
                    throw new IOException("Output is over 4 GiB, too big for FAT32. Use UL format or an exFAT drive.");
            }
            File.Move(temp, target);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
        return new IsoGame(target, name, info.GameId, media, format, new FileInfo(target).Length);
    }

    public IsoGame RenameGame(IsoGame game, string newName)
    {
        ValidateName(newName);
        var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(game.Path)!, newName + System.IO.Path.GetExtension(game.Path));
        if (!target.Equals(game.Path, StringComparison.Ordinal))
            File.Move(game.Path, target);
        return game with { Path = target, Name = newName };
    }

    public void DeleteGame(IsoGame game) => File.Delete(game.Path);

    /// <summary>"SLUS_123.45.Name" → (SLUS_123.45, Name); other names → (null, name).</summary>
    public static (string? GameId, string Name) SplitOldStyleName(string fileName) =>
        fileName.Length > 12 && fileName[4] == '_' && fileName[8] == '.' && fileName[11] == '.'
            ? (fileName[..11], fileName[12..])
            : (null, fileName);

    private static string? TryReadGameId(string path)
    {
        try
        {
            return Ps2Iso.Read(path).GameId;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException)
        {
            return null;
        }
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MaxNameLength)
            throw new ArgumentException($"Max {MaxNameLength} chars.", nameof(name));
        if (name != name.Trim() || name.EndsWith('.'))
            throw new ArgumentException("Name can't start/end with spaces or end with a dot.", nameof(name));
        foreach (var c in name)
        {
            if (c is < ' ' or > '~' || FatInvalidChars.Contains(c))
                throw new ArgumentException($"Name has a char not allowed in file names: '{c}'.", nameof(name));
        }
    }

    private bool IsFat32()
    {
        try
        {
            return new DriveInfo(System.IO.Path.GetPathRoot(Root)!).DriveFormat.StartsWith("FAT", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void EnsureFreeSpace(long needed)
    {
        long free;
        try
        {
            free = new DriveInfo(System.IO.Path.GetPathRoot(Root)!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (free < needed)
            throw new IOException($"Not enough space: need {needed:N0} bytes, {free:N0} free.");
    }
}
