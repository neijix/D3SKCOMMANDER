using D3skCommander.App.Features.Drives;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Panels;

/// <summary>One button of the drive bar. <see cref="Holds"/> marks the drive that holds the panel's folder.</summary>
public sealed record DriveButton(string Path, string Text, string Tip, bool Holds);

/// <summary>
/// The buttons above a panel. The volumes are polled on a worker every few seconds; <see cref="Update"/>
/// (called on the UI beat, no I/O) says whether the buttons changed.
/// </summary>
public sealed class DriveBarModel
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(3);

    private readonly IDriveSource _source;
    private readonly ILocalizer _text;
    private readonly TimeProvider _clock;
    private IReadOnlyList<DriveEntry> _drives = [];
    private IReadOnlyList<DriveEntry>? _applied;
    private string _path = "\0";
    private DateTimeOffset _next = DateTimeOffset.MinValue;
    private int _busy;

    public DriveBarModel(IDriveSource source, ILocalizer text, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(text);
        _source = source;
        _text = text;
        _clock = clock ?? TimeProvider.System;
    }

    public IReadOnlyList<DriveButton> Buttons { get; private set; } = [];

    /// <summary>Starts a poll when one is due, then rebuilds the buttons if the list or the folder changed.</summary>
    public bool Update(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Poll();
        var drives = Volatile.Read(ref _drives);
        if (ReferenceEquals(drives, _applied) && path == _path)
        {
            return false;
        }

        _applied = drives;
        _path = path;
        var holder = DriveList.Holding(drives, path);
        var next = new List<DriveButton>(drives.Count);
        foreach (var drive in drives)
        {
            next.Add(new DriveButton(drive.Path, DriveList.ShortName(drive.Path), TipOf(drive), ReferenceEquals(drive, holder)));
        }

        if (next.SequenceEqual(Buttons))
        {
            return false;
        }

        Buttons = next;
        return true;
    }

    private void Poll()
    {
        var now = _clock.GetUtcNow();
        if (now < _next || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        _next = now + Every;
        _ = Task.Run(() =>
        {
            try
            {
                var found = _source.Mounted();
                if (!found.SequenceEqual(Volatile.Read(ref _drives)))
                {
                    Volatile.Write(ref _drives, found);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A volume that cannot be read keeps the last list.
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private string TipOf(DriveEntry drive)
    {
        var tip = drive.Label.Length == 0 || drive.Label == drive.Path ? drive.Path : drive.Label + "\n" + drive.Path;
        if (drive.TotalBytes == 0)
        {
            return tip;
        }

        var free = _text.Get("drives.free_of")
            .Replace("{0}", LocalFileSystem.FormatSize(drive.FreeBytes), StringComparison.Ordinal)
            .Replace("{1}", LocalFileSystem.FormatSize(drive.TotalBytes), StringComparison.Ordinal);
        return tip + "\n" + free;
    }
}
