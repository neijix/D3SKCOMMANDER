using D3skCommander.Core.NetDrives;

namespace D3skCommander.Core.Settings;

/// <summary>A favorite's path as the user types it: cleaned up for saving, and the name it gets by default.</summary>
public static class HotlistPath
{
    /// <summary>
    /// The path to save for <paramref name="typed"/>: trimmed, without surrounding quotes; a network server
    /// (<c>\\server</c>, <c>\\server\</c>, either separator) becomes its canonical root <c>\\server\</c>.
    /// Null when nothing is left.
    /// </summary>
    public static string? Clean(string? typed)
    {
        var text = (typed ?? "").Trim().Trim('"').Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return UncPath.Server(text) is { } server ? UncPath.ServerRoot(server) : text;
    }

    /// <summary>The last part of the path (<c>server</c> for <c>\\server\</c>, <c>share</c> for <c>\\server\share</c>); the path itself for a root.</summary>
    public static string DefaultName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 || parts[^1].EndsWith(':') ? path : parts[^1];
    }
}
