namespace D3skCommander.App.Features.Panels;

/// <summary>Cheap signature of a folder: its timestamp and how many names it holds.</summary>
internal static class PanelWatch
{
    public static bool TryRead(string path, out long stamp)
    {
        stamp = 0;
        try
        {
            var ticks = Directory.GetLastWriteTimeUtc(path).Ticks;
            var count = Directory.EnumerateFileSystemEntries(path).Count();
            stamp = HashCode.Combine(ticks, count);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
