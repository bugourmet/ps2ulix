namespace Ps2Ulix.Core;

public static class SizeFormat
{
    /// <summary>Human-readable binary size, e.g. <c>4.38 GiB</c>.</summary>
    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GiB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MiB",
        _ => $"{bytes / 1024.0:0.0} KiB",
    };
}
