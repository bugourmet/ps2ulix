using System.Text;

namespace Ps2Ulix.Core;

/// <summary>OPL compatibility modes ($Compatibility bitmask, OPL <c>include/iosupport.h</c>).</summary>
[Flags]
public enum CompatModes
{
    None = 0,
    AccurateReads = 0x01,
    SyncReads = 0x02,
    UnhookSyscalls = 0x04,
    SkipVideos = 0x08,
    EmulateDvdDl = 0x10,
    DisableIgr = 0x20,
}

/// <summary>
/// Per-game OPL settings in <c>CFG/&lt;startup&gt;.cfg</c>: CRLF <c>key=value</c> lines, split at the first '='
/// (OPL <c>src/config.c</c>). Unknown keys are kept as-is.
/// </summary>
public sealed class OplGameConfig
{
    public const string Folder = "CFG";
    public const string CompatibilityKey = "$Compatibility";
    public const string DmaKey = "$DMA";
    public const string AltStartupKey = "$AltStartup";
    public const string Vmc0Key = "$VMC_0";
    public const string Vmc1Key = "$VMC_1";

    /// <summary>OPL's default DMA mode (UDMA 4); it removes the key instead of writing this.</summary>
    public const int DefaultDma = 7;

    public static readonly string[] DmaModeNames = ["MDMA 0", "MDMA 1", "MDMA 2", "UDMA 0", "UDMA 1", "UDMA 2", "UDMA 3", "UDMA 4"];

    private const int MaxKeyLength = 31;
    private const int MaxValueLength = 255;

    private readonly List<KeyValuePair<string, string>> _entries = [];

    public static string GetPath(string root, string startup) => Path.Combine(root, Folder, startup + ".cfg");

    /// <summary>Loads a game's config; missing file means empty config.</summary>
    public static OplGameConfig Load(string root, string startup)
    {
        var config = new OplGameConfig();
        var path = GetPath(root, startup);
        if (!File.Exists(path))
            return config;
        foreach (var line in File.ReadAllLines(path, Encoding.ASCII))
        {
            var text = line.TrimStart(' ', '\t');
            var eq = text.IndexOf('=');
            if (eq > 0)
                config.Set(text[..eq], text[(eq + 1)..]);
        }
        return config;
    }

    /// <summary>Writes the config like OPL: CRLF lines, '#' keys dropped. Deletes the file when nothing is left.</summary>
    public void Save(string root, string startup)
    {
        var path = GetPath(root, startup);
        var lines = _entries.Where(e => !e.Key.StartsWith('#')).Select(e => $"{e.Key}={e.Value}\r\n").ToList();
        if (lines.Count == 0)
        {
            File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Concat(lines), Encoding.ASCII);
    }

    public IReadOnlyList<KeyValuePair<string, string>> Entries => _entries;

    public string? Get(string key) => _entries.FirstOrDefault(e => e.Key == key).Value;

    /// <summary>Sets a key; null removes it.</summary>
    public void Set(string key, string? value)
    {
        if (key.Length == 0 || key.Length > MaxKeyLength || key.Contains('='))
            throw new ArgumentException($"Invalid key '{key}'.", nameof(key));
        var index = _entries.FindIndex(e => e.Key == key);
        if (value is null)
        {
            if (index >= 0)
                _entries.RemoveAt(index);
            return;
        }
        if (value.Length > MaxValueLength || value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException($"Invalid value for '{key}'.", nameof(value));
        if (index >= 0)
            _entries[index] = new(key, value);
        else
            _entries.Add(new(key, value));
    }

    public CompatModes Compatibility
    {
        get => int.TryParse(Get(CompatibilityKey), out var v) ? (CompatModes)v : CompatModes.None;
        set => Set(CompatibilityKey, value == CompatModes.None ? null : ((int)value).ToString());
    }

    /// <summary>0..7 = MDMA 0-2, UDMA 0-4 (only used for HDD/ATA). Default 7 is stored by removing the key.</summary>
    public int Dma
    {
        get => int.TryParse(Get(DmaKey), out var v) && v is >= 0 and <= DefaultDma ? v : DefaultDma;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, DefaultDma);
            Set(DmaKey, value == DefaultDma ? null : value.ToString());
        }
    }

    /// <summary>Boot ELF override; empty removes it.</summary>
    public string? AltStartup
    {
        get => Get(AltStartupKey);
        set => Set(AltStartupKey, string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>Virtual memory card name for slot 1 (file VMC/&lt;name&gt;.bin); empty removes it.</summary>
    public string? Vmc0
    {
        get => Get(Vmc0Key);
        set => Set(Vmc0Key, string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>Virtual memory card name for slot 2; empty removes it.</summary>
    public string? Vmc1
    {
        get => Get(Vmc1Key);
        set => Set(Vmc1Key, string.IsNullOrEmpty(value) ? null : value);
    }
}
