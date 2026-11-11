namespace Ps2Ulix.Core;

/// <summary>Parses the disc's SYSTEM.CNF (e.g. <c>BOOT2 = cdrom0:\SLUS_209.46;1</c>).</summary>
public static class SystemCnf
{
    public const string FileName = "SYSTEM.CNF";

    /// <summary>Returns the boot ELF name from BOOT2, e.g. <c>SLUS_209.46</c>.</summary>
    /// <exception cref="InvalidDataException">No BOOT2 line (PS1 discs use BOOT) or empty path.</exception>
    public static string ParseGameId(string text)
    {
        var isPs1 = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var eq = rawLine.IndexOf('=');
            if (eq < 0)
                continue;
            var key = rawLine[..eq].Trim();
            if (key.Equals("BOOT", StringComparison.OrdinalIgnoreCase))
                isPs1 = true;
            if (!key.Equals("BOOT2", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = rawLine[(eq + 1)..].Trim().TrimEnd('\0');
            var start = path.LastIndexOfAny([':', '\\', '/']) + 1;
            var version = path.IndexOf(';', start);
            var id = (version < 0 ? path[start..] : path[start..version]).Trim();
            if (id.Length == 0)
                throw new InvalidDataException("BOOT2 has no ELF name.");
            return id.ToUpperInvariant();
        }
        throw new InvalidDataException(isPs1 ? "PS1 disc (BOOT, not BOOT2); not supported." : "SYSTEM.CNF has no BOOT2 line.");
    }
}
