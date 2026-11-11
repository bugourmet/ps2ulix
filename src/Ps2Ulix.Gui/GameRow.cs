using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Ps2Ulix.Core;
using SkiaSharp;

namespace Ps2Ulix.Gui;

/// <summary>One game in the library: a UL game or a CD/DVD folder image.</summary>
public sealed partial class GameRow : ObservableObject
{
    private const int CoverDecodeWidth = 440;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private Bitmap? _cover;

    public GameRow(UlDrive drive, UlGameEntry entry)
    {
        Ul = entry;
        var parts = Enumerable.Range(0, entry.Parts).Select(p => new FileInfo(drive.GetPartPath(entry, p))).ToList();
        var missing = parts.Count(f => !f.Exists);
        SizeBytes = parts.Where(f => f.Exists).Sum(f => f.Length);
        Name = entry.Name;
        GameId = entry.Startup;
        MediaValue = entry.Media;
        Format = $"UL ({entry.Parts})";
        Status = missing == 0 ? "OK" : $"{missing} part(s) missing";
    }

    public GameRow(IsoGame game)
    {
        Iso = game;
        SizeBytes = game.FileSize;
        Name = game.Name;
        GameId = game.GameId;
        MediaValue = game.Media;
        Format = game.Format.ToString().ToUpperInvariant();
        Status = game.GameId is null ? "Unreadable image" : "OK";
    }

    public UlGameEntry? Ul { get; }
    public IsoGame? Iso { get; }
    public string Name { get; }
    public string? GameId { get; }
    public UlMedia MediaValue { get; }
    public string Media => MediaValue switch { UlMedia.Cd => "CD", UlMedia.Dvd => "DVD", var m => $"0x{(byte)m:X2}" };
    public string Format { get; }
    public long SizeBytes { get; }
    public string Size => SizeFormat.Format(SizeBytes);
    public string Status { get; }

    public bool IsDvd => MediaValue == UlMedia.Dvd;
    public bool IsUl => Ul is not null;
    public bool IsIso => Iso?.Format == IsoFormat.Iso;
    public bool IsZso => Iso?.Format == IsoFormat.Zso;
    public bool HasProblem => Status != "OK";
    public bool HasCover => Cover is not null;
    public string DisplayId => GameId ?? "unknown ID";

    /// <summary>Colour of the cover, tinting the backdrop while the game is selected.</summary>
    public Color? CoverColor { get; private set; }

    /// <summary>Stable identity across reloads.</summary>
    public string Key => $"{Format}|{Name}|{GameId}";

    /// <summary>Decodes the cover from ART/ if there is one; a broken image counts as none. Safe off the UI thread.</summary>
    public (Bitmap? Cover, Color? Color) ReadCover(string root)
    {
        if (GameId is null)
            return default;
        var path = ArtDownloader.GetPath(root, GameId, "COV");
        if (!File.Exists(path))
            path = Path.ChangeExtension(path, ".jpg");
        if (!File.Exists(path))
            return default;
        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = Bitmap.DecodeToWidth(stream, CoverDecodeWidth);
            return (bitmap, DominantColor(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NullReferenceException)
        {
            return default;
        }
    }

    public void SetCover(Bitmap? cover, Color? color)
    {
        CoverColor = color;
        Cover = cover;
    }

    /// <summary>Average weighted by saturation, lifted so dark covers still light the fog.</summary>
    private static Color? DominantColor(string path)
    {
        using var image = SKBitmap.Decode(path);
        if (image is null)
            return null;
        double r = 0, g = 0, b = 0, weight = 0;
        var stepX = Math.Max(1, image.Width / 24);
        var stepY = Math.Max(1, image.Height / 32);
        for (var y = 0; y < image.Height; y += stepY)
        {
            for (var x = 0; x < image.Width; x += stepX)
            {
                var c = image.GetPixel(x, y);
                var max = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                var min = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                var w = (max - min + 1.0) * (max + 1.0);
                r += c.Red * w;
                g += c.Green * w;
                b += c.Blue * w;
                weight += w;
            }
        }
        if (weight <= 0)
            return null;
        var hsv = Color.FromRgb((byte)(r / weight), (byte)(g / weight), (byte)(b / weight)).ToHsv();
        return HsvColor.ToRgb(hsv.H, Math.Clamp(hsv.S * 1.25, 0.25, 0.85), Math.Clamp(hsv.V, 0.75, 1));
    }
}

/// <summary>Entry in the drive rail.</summary>
/// <param name="UsedFraction">0..1 of the volume in use.</param>
public sealed record DriveItem(string Path, string Title, string Detail, double UsedFraction, bool IsRemovable, long Total, long Free)
{
    public Controls.DeviceKind Kind => IsRemovable ? Controls.DeviceKind.Usb
        : Detail.StartsWith("Folder", StringComparison.Ordinal) ? Controls.DeviceKind.Folder : Controls.DeviceKind.Disk;

    public override string ToString() => Title;

    public static DriveItem FromInfo(DriveInfo d)
    {
        var title = string.IsNullOrEmpty(d.VolumeLabel) ? d.Name.TrimEnd('\\', '/') : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\', '/')})";
        return new DriveItem(d.RootDirectory.FullName, title, $"{d.DriveFormat} · {SizeFormat.Format(d.AvailableFreeSpace)} free",
            d.TotalSize > 0 ? 1 - (double)d.TotalFreeSpace / d.TotalSize : 0, d.DriveType == DriveType.Removable, d.TotalSize, d.AvailableFreeSpace);
    }

    public static DriveItem FromFolder(string folder)
    {
        var name = System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'));
        try
        {
            var d = new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(folder))!);
            return new DriveItem(folder, name.Length > 0 ? name : folder, $"Folder · {SizeFormat.Format(d.AvailableFreeSpace)} free",
                d.TotalSize > 0 ? 1 - (double)d.TotalFreeSpace / d.TotalSize : 0, false, d.TotalSize, d.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new DriveItem(folder, name, "Folder", 0, false, 0, 0);
        }
    }
}
