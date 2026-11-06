using System.Globalization;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneModel
{
    private static readonly long s_freeLifetime = TimeSpan.FromSeconds(5).Ticks;

    // The free space of the folder's volume: read off the UI thread, reused for a few seconds.
    private FreeReading _free = new("", null, 0);
    private bool _freeBusy;

    private sealed record FreeReading(string Path, ulong? Bytes, long At);

    /// <summary>"2 / 13 selected · 1.2 GB / 4.5 GB · 1.5 TB free"; the free part only for a local folder. No I/O.</summary>
    private string StatusOf(PanelSnapshot snap)
    {
        var text = _text.Get("panels.selected")
            .Replace("{0}", snap.Selected.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{1}", snap.Selectable.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        text += " · " + LocalFileSystem.FormatSize(snap.SelectedBytes) + " / " + LocalFileSystem.FormatSize(snap.TotalBytes);
        var free = FreeFor(snap.Path);
        return free is { } bytes
            ? text + " · " + _text.Get("panels.free").Replace("{0}", LocalFileSystem.FormatSize(bytes), StringComparison.Ordinal)
            : text;
    }

    private ulong? FreeFor(string path)
    {
        var reading = Volatile.Read(ref _free);
        var stale = reading.Path != path || Environment.TickCount64 * TimeSpan.TicksPerMillisecond - reading.At > s_freeLifetime;
        if (stale && path.Length > 0 && !_freeBusy)
        {
            _freeBusy = true;
            _ = Task.Run(() =>
            {
                var bytes = PanelTotals.FreeSpace(path);
                Volatile.Write(ref _free, new FreeReading(path, bytes, Environment.TickCount64 * TimeSpan.TicksPerMillisecond));
                _freeBusy = false;
            });
        }

        return reading.Path == path ? reading.Bytes : null;
    }
}
