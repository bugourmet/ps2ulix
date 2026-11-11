using Ps2Ulix.Core;

const string Usage = """
    Usage:
      ps2ulix info    <iso|zso>
      ps2ulix list    <drive>
      ps2ulix add     <drive> <iso|zso> [--name <name>] [--cd | --dvd] [--iso | --zso]
      ps2ulix join    <drive> <game> <output.iso>
      ps2ulix delete  <drive> <game>
      ps2ulix rename  <drive> <game> <new-name>
      ps2ulix convert <drive> <game> --to ul|iso|zso [--move]
      ps2ulix check   <drive>
      ps2ulix rebuild <drive> [--dry-run]
      ps2ulix compress   <in.iso> <out.zso>
      ps2ulix decompress <in.zso> <out.iso>
      ps2ulix art     <drive> [<game>] [--all] [--force]
      ps2ulix config  <drive> <game> [--compat 1,3,4|none] [--dma 0-7] [--vmc0 <name>] [--vmc1 <name>] [--alt-startup <id>]

    <drive>  USB drive root or folder with ul.cfg / CD / DVD
    <game>   exact game name or game ID (e.g. SLUS_209.46)
    add      default is UL format (split parts); --iso/--zso puts one file in CD/ or DVD/
    art      downloads covers (--all: also back, label, logo) for one game or every game
    compat   OPL modes: 1 accurate reads, 2 sync reads, 3 unhook syscalls, 4 skip videos,
             5 emulate DVD-DL, 6 disable IGR
    """;

string[] valueOptions = ["name", "to", "compat", "dma", "vmc0", "vmc1", "alt-startup"];
string[] flagOptions = ["cd", "dvd", "iso", "zso", "dry-run", "move", "all", "force"];

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var (positional, options) = ParseArgs(args.Skip(1), valueOptions, flagOptions);
    switch (args[0])
    {
        case "info" when positional.Count == 1:
        {
            var info = Ps2Iso.Read(positional[0]);
            Console.WriteLine($"Game ID: {info.GameId}");
            Console.WriteLine($"Media:   {info.Media}");
            Console.WriteLine($"Volume:  {info.VolumeId}");
            Console.WriteLine($"Size:    {SizeFormat.Format(info.Size)}");
            Console.WriteLine($"Name:    {UlDrive.NameFromFile(positional[0])}");
            return 0;
        }

        case "list" when positional.Count == 1:
        {
            var drive = new UlDrive(positional[0]);
            var ul = drive.LoadGames();
            var iso = new IsoFolders(positional[0]).LoadGames();
            if (ul.Count + iso.Count == 0)
            {
                Console.WriteLine("No games.");
                return 0;
            }
            Console.WriteLine($"{"Name",-32}  {"ID",-12}  {"Media",-5}  {"Format",-6}  {"Size",10}");
            foreach (var g in ul)
                Console.WriteLine($"{g.Name,-32}  {g.Startup,-12}  {g.Media,-5}  {$"UL/{g.Parts}",-6}  {SizeFormat.Format(UlSize(drive, g)),10}");
            foreach (var g in iso)
                Console.WriteLine($"{g.Name,-32}  {g.GameId ?? "?",-12}  {g.Media,-5}  {g.Format.ToString().ToUpperInvariant(),-6}  {SizeFormat.Format(g.FileSize),10}");
            return 0;
        }

        case "add" when positional.Count == 2:
        {
            UlMedia? media = options.ContainsKey("cd") ? UlMedia.Cd : options.ContainsKey("dvd") ? UlMedia.Dvd : null;
            var size = Ps2Iso.Read(positional[1]).Size;
            if (IsoFormatOption(options) is { } format)
            {
                var added = new IsoFolders(positional[0]).AddGame(positional[1], options.GetValueOrDefault("name"), media,
                    format, ProgressBar(size), cts.Token);
                Console.Error.WriteLine();
                Console.WriteLine($"Added {added.Path} ({SizeFormat.Format(added.FileSize)}).");
            }
            else
            {
                var added = new UlDrive(positional[0]).AddGame(positional[1], options.GetValueOrDefault("name"), media,
                    ProgressBar(size), cts.Token);
                Console.Error.WriteLine();
                Console.WriteLine($"Added '{added.Name}' ({added.Startup}, {added.Media}, {added.Parts} part(s)).");
            }
            return 0;
        }

        case "join" when positional.Count == 3:
        {
            var drive = new UlDrive(positional[0]);
            var game = drive.FindGame(positional[1]);
            drive.JoinGame(game, positional[2], ProgressBar(UlSize(drive, game)), cts.Token);
            Console.Error.WriteLine();
            Console.WriteLine($"Wrote {positional[2]}.");
            return 0;
        }

        case "delete" when positional.Count == 2:
        {
            var (drive, folders, ul, iso) = FindGame(positional[0], positional[1]);
            if (ul is not null)
                drive.DeleteGame(ul);
            else
                folders.DeleteGame(iso!);
            Console.WriteLine($"Deleted '{ul?.Name ?? iso!.Name}'.");
            return 0;
        }

        case "rename" when positional.Count == 3:
        {
            var (drive, folders, ul, iso) = FindGame(positional[0], positional[1]);
            var newName = ul is not null ? drive.RenameGame(ul, positional[2]).Name : folders.RenameGame(iso!, positional[2]).Name;
            Console.WriteLine($"Renamed '{ul?.Name ?? iso!.Name}' -> '{newName}'.");
            return 0;
        }

        case "convert" when positional.Count == 2 && options.GetValueOrDefault("to") is { } to:
        {
            var (drive, folders, ul, iso) = FindGame(positional[0], positional[1]);
            var move = options.ContainsKey("move");
            if (to.Equals("ul", StringComparison.OrdinalIgnoreCase))
            {
                if (iso is null)
                    throw new InvalidOperationException("Game is already in UL format.");
                var size = Ps2Iso.Read(iso.Path).Size;
                var added = drive.AddGame(iso.Path, UlNameFrom(iso.Name), iso.Media, ProgressBar(size), cts.Token);
                if (move)
                    folders.DeleteGame(iso);
                Console.Error.WriteLine();
                Console.WriteLine($"Converted to UL '{added.Name}' ({added.Parts} part(s)).");
            }
            else
            {
                var format = to.ToLowerInvariant() switch
                {
                    "iso" => IsoFormat.Iso,
                    "zso" => IsoFormat.Zso,
                    _ => throw new ArgumentException("--to must be ul, iso or zso."),
                };
                IsoGame added;
                if (ul is not null)
                {
                    Ps2IsoInfo info;
                    using (var image = drive.OpenGameImage(ul))
                        info = Ps2Iso.Read(image);
                    added = folders.AddGame(() => drive.OpenGameImage(ul), info, ul.Name, ul.Media, format, ProgressBar(info.Size), cts.Token);
                    if (move)
                        drive.DeleteGame(ul);
                }
                else
                {
                    if (iso!.Format == format)
                        throw new InvalidOperationException($"Game is already {format.ToString().ToUpperInvariant()}.");
                    var info = Ps2Iso.Read(iso.Path);
                    // Same folder and name, other extension; the source is only removed after the copy succeeds.
                    added = folders.AddGame(() => Ps2Iso.OpenImage(iso.Path), info, iso.Name, iso.Media, format, ProgressBar(info.Size), cts.Token);
                    if (move)
                        folders.DeleteGame(iso);
                }
                Console.Error.WriteLine();
                Console.WriteLine($"Wrote {added.Path} ({SizeFormat.Format(added.FileSize)}).");
            }
            return 0;
        }

        case "compress" when positional.Count == 2:
        case "decompress" when positional.Count == 2:
        {
            var compress = args[0] == "compress";
            if (File.Exists(positional[1]))
                throw new IOException($"Output already exists: {positional[1]}");
            using (var input = File.OpenRead(positional[0]))
            {
                long total;
                if (compress)
                    total = input.Length;
                else
                    using (var probe = new ZsoReader(input, leaveOpen: true))
                        total = probe.Length;
                try
                {
                    using var output = new FileStream(positional[1], FileMode.CreateNew, FileAccess.ReadWrite);
                    if (compress)
                        Zso.Compress(input, output, ProgressBar(total), cts.Token);
                    else
                        Zso.Decompress(input, output, ProgressBar(total), cts.Token);
                }
                catch
                {
                    File.Delete(positional[1]);
                    throw;
                }
            }
            Console.Error.WriteLine();
            Console.WriteLine($"Wrote {positional[1]} ({SizeFormat.Format(new FileInfo(positional[1]).Length)}).");
            return 0;
        }

        case "art" when positional.Count is 1 or 2:
        {
            var root = new UlDrive(positional[0]).Root;
            IEnumerable<string> ids;
            if (positional.Count == 2)
            {
                var (_, _, ul, iso) = FindGame(positional[0], positional[1]);
                ids = [ul?.Startup ?? iso!.GameId ?? throw new InvalidOperationException("Game ID unknown (image unreadable).")];
            }
            else
            {
                ids = new UlDrive(root).LoadGames().Select(g => g.Startup)
                    .Concat(new IsoFolders(root).LoadGames().Select(g => g.GameId).OfType<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            }
            string[] suffixes = options.ContainsKey("all") ? ArtDownloader.AllSuffixes : ["COV"];
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ps2ulix");
            var downloader = new ArtDownloader(http);
            foreach (var id in ids)
            {
                var results = await downloader.DownloadAsync(root, id, suffixes, options.ContainsKey("force"), cts.Token);
                Console.WriteLine($"{id,-12}  " + string.Join("  ", results.Select(r => $"{r.Suffix}:{r.Outcome switch
                {
                    ArtOutcome.Downloaded => "ok",
                    ArtOutcome.AlreadyExists => "have",
                    _ => "none",
                }}")));
            }
            return 0;
        }

        case "config" when positional.Count == 2:
        {
            var (drive, _, ul, iso) = FindGame(positional[0], positional[1]);
            var id = ul?.Startup ?? iso!.GameId ?? throw new InvalidOperationException("Game ID unknown (image unreadable).");
            var config = OplGameConfig.Load(drive.Root, id);
            var changed = false;
            if (options.TryGetValue("compat", out var compat))
            {
                config.Compatibility = ParseCompat(compat!);
                changed = true;
            }
            if (options.TryGetValue("dma", out var dma))
            {
                config.Dma = int.TryParse(dma, out var d) ? d : throw new ArgumentException("--dma must be 0-7.");
                changed = true;
            }
            foreach (var (option, apply) in new (string, Action<string>)[]
                     {
                         ("vmc0", v => config.Vmc0 = v), ("vmc1", v => config.Vmc1 = v), ("alt-startup", v => config.AltStartup = v),
                     })
            {
                if (options.TryGetValue(option, out var value))
                {
                    apply(value!);
                    changed = true;
                }
            }
            if (changed)
                config.Save(drive.Root, id);

            Console.WriteLine($"{OplGameConfig.GetPath(drive.Root, id)}{(changed ? " (saved)" : "")}");
            var modes = Enumerable.Range(0, 6).Where(i => config.Compatibility.HasFlag((CompatModes)(1 << i))).Select(i => $"{i + 1} {(CompatModes)(1 << i)}");
            Console.WriteLine($"  Compat modes: {(config.Compatibility == CompatModes.None ? "none" : string.Join(", ", modes))}");
            Console.WriteLine($"  DMA:          {OplGameConfig.DmaModeNames[config.Dma]}");
            Console.WriteLine($"  VMC slot 1/2: {config.Vmc0 ?? "-"} / {config.Vmc1 ?? "-"}");
            Console.WriteLine($"  Alt startup:  {config.AltStartup ?? "-"}");
            foreach (var (key, value) in config.Entries.Where(e => e.Key is not (OplGameConfig.CompatibilityKey or OplGameConfig.DmaKey
                         or OplGameConfig.Vmc0Key or OplGameConfig.Vmc1Key or OplGameConfig.AltStartupKey)))
                Console.WriteLine($"  {key}={value}");
            return 0;
        }

        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException
                               or ArgumentException or UnauthorizedAccessException or HttpRequestException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static (List<string> Positional, Dictionary<string, string?> Options) ParseArgs(IEnumerable<string> args,
    string[] valueOptions, string[] flagOptions)
{
    var positional = new List<string>();
    var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    using var e = args.GetEnumerator();
    while (e.MoveNext())
    {
        if (!e.Current.StartsWith("--"))
        {
            positional.Add(e.Current);
            continue;
        }
        var key = e.Current[2..];
        if (valueOptions.Contains(key))
            options[key] = e.MoveNext() ? e.Current : throw new ArgumentException($"--{key} needs a value.");
        else if (flagOptions.Contains(key))
            options[key] = null;
        else
            throw new ArgumentException($"Unknown option --{key}.");
    }
    if (options.ContainsKey("cd") && options.ContainsKey("dvd"))
        throw new ArgumentException("Use --cd or --dvd, not both.");
    if (options.ContainsKey("iso") && options.ContainsKey("zso"))
        throw new ArgumentException("Use --iso or --zso, not both.");
    return (positional, options);
}

static IsoFormat? IsoFormatOption(Dictionary<string, string?> options) =>
    options.ContainsKey("iso") ? IsoFormat.Iso : options.ContainsKey("zso") ? IsoFormat.Zso : null;

static long UlSize(UlDrive drive, UlGameEntry game) => Enumerable.Range(0, game.Parts)
    .Select(p => new FileInfo(drive.GetPartPath(game, p))).Sum(f => f.Exists ? f.Length : 0);

/// <summary>Finds a game by name or ID in UL format or CD/DVD folders; exactly one match allowed.</summary>
static (UlDrive Drive, IsoFolders Folders, UlGameEntry? Ul, IsoGame? Iso) FindGame(string root, string nameOrId)
{
    var drive = new UlDrive(root);
    var folders = new IsoFolders(root);
    bool Match(string? s) => s is not null && s.Equals(nameOrId, StringComparison.OrdinalIgnoreCase);
    var ul = drive.LoadGames().Where(g => Match(g.Name) || Match(g.Startup)).ToList();
    var iso = folders.LoadGames().Where(g => Match(g.Name) || Match(g.GameId)).ToList();
    return (ul.Count + iso.Count) switch
    {
        1 => (drive, folders, ul.SingleOrDefault(), iso.SingleOrDefault()),
        0 => throw new InvalidOperationException($"No game '{nameOrId}'."),
        var n => throw new InvalidOperationException($"'{nameOrId}' matches {n} games; use the exact name."),
    };
}

/// <summary>ISO names can be longer and non-ASCII-free; UL names are max 32 ASCII chars.</summary>
static string UlNameFrom(string isoName) => UlDrive.NameFromFile(isoName + ".iso");

static CompatModes ParseCompat(string value)
{
    if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || value == "0")
        return CompatModes.None;
    var modes = CompatModes.None;
    foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!int.TryParse(part, out var n) || n is < 1 or > 6)
            throw new ArgumentException($"Compat mode '{part}' must be 1-6.");
        modes |= (CompatModes)(1 << (n - 1));
    }
    return modes;
}

static IProgress<long> ProgressBar(long total)
{
    var last = -1;
    return new SyncProgress(done =>
    {
        var percent = total == 0 ? 100 : (int)(done * 100 / total);
        if (percent == last)
            return;
        last = percent;
        Console.Error.Write($"\r[{new string('#', percent / 4),-25}] {percent,3}%");
    });
}

sealed class SyncProgress(Action<long> report) : IProgress<long>
{
    public void Report(long value) => report(value);
}
