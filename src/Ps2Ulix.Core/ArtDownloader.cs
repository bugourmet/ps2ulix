namespace Ps2Ulix.Core;

public enum ArtOutcome
{
    Downloaded,
    AlreadyExists,
    NotFound,
}

public sealed record ArtResult(string Suffix, ArtOutcome Outcome, string Path);

/// <summary>
/// Downloads OPL art into <c>ART/&lt;ID&gt;_&lt;suffix&gt;.png</c>. Default source is a GitHub mirror of the
/// OPL Manager art database, which uses the same names (<c>PS2/&lt;ID&gt;/&lt;ID&gt;_COV.png</c>).
/// The art is third-party with no stated license.
/// </summary>
public sealed class ArtDownloader(HttpClient http, string baseUrl = ArtDownloader.DefaultBaseUrl)
{
    public const string DefaultBaseUrl = "https://raw.githubusercontent.com/Luden02/psx-ps2-opl-art-database/main/PS2/";
    public const string Folder = "ART";

    /// <summary>Front cover, back cover, disc label, logo.</summary>
    public static readonly string[] AllSuffixes = ["COV", "COV2", "LAB", "LGO"];

    private static ReadOnlySpan<byte> PngMagic => [0x89, (byte)'P', (byte)'N', (byte)'G'];

    public static string GetPath(string root, string gameId, string suffix) =>
        System.IO.Path.Combine(root, Folder, $"{gameId}_{suffix}.png");

    public async Task<List<ArtResult>> DownloadAsync(string root, string gameId, IEnumerable<string> suffixes,
        bool overwrite = false, CancellationToken cancellationToken = default)
    {
        if (gameId.Length == 0 || gameId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-')))
            throw new ArgumentException($"Invalid game ID '{gameId}'.", nameof(gameId));

        var results = new List<ArtResult>();
        foreach (var suffix in suffixes)
        {
            var path = GetPath(root, gameId, suffix);
            if (File.Exists(path) && !overwrite)
            {
                results.Add(new ArtResult(suffix, ArtOutcome.AlreadyExists, path));
                continue;
            }

            using var response = await http.GetAsync($"{baseUrl}{gameId}/{gameId}_{suffix}.png", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                results.Add(new ArtResult(suffix, ArtOutcome.NotFound, path));
                continue;
            }
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!bytes.AsSpan().StartsWith(PngMagic))
                throw new InvalidDataException($"{gameId}_{suffix}: server did not return a PNG.");

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            results.Add(new ArtResult(suffix, ArtOutcome.Downloaded, path));
        }
        return results;
    }
}
