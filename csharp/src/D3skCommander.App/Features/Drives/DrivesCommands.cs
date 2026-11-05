using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.NetDrives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;

namespace D3skCommander.App.Features.Drives;

/// <summary>Alt+F1 and Alt+F2 drive lists, Properties (Alt+Enter) and files dropped from the OS.</summary>
public sealed partial class DrivesCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly IDriveSource _drives;
    private readonly INetDriveRunner _shares;
    private readonly FileOperationService _files;
    private readonly IFileSystemProvider _provider;
    private int _busy;

    public DrivesCommands(
        IPanelWorkspace panels,
        IDialogService dialogs,
        ILocalizer text,
        IJobService jobs,
        IDriveSource drives,
        INetDriveRunner shares,
        FileOperationService files,
        IFileSystemProvider provider)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(provider);
        _panels = panels;
        _dialogs = dialogs;
        _text = text;
        _jobs = jobs;
        _drives = drives;
        _shares = shares;
        _files = files;
        _provider = provider;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.DrivesLeft, () => _ = ChooseDriveAsync(PanelSide.Left));
        shell.Handle(CommandId.DrivesRight, () => _ = ChooseDriveAsync(PanelSide.Right));
        shell.Handle(CommandId.Properties, () => _ = PropertiesAsync());
        if (_panels is IFileDrop drop)
        {
            drop.FilesDropped = DropAsync;
        }
    }

    /// <summary>Lists the volumes and connected shares for <paramref name="side"/> and opens the one chosen.</summary>
    public async Task ChooseDriveAsync(PanelSide side)
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            var title = _text.Get(side == PanelSide.Left ? "cmd.drives_left" : "cmd.drives_right");
            var places = await Task.Run(Places).ConfigureAwait(false);
            if (places.Count == 0)
            {
                await _dialogs.ShowMessageAsync(title, _text.Get("drives.none")).ConfigureAwait(false);
                return;
            }

            var labels = places.Select(place => place.Label).ToList();
            var picked = await _dialogs.ChooseAsync(title, _text.Get("drives.choose"), labels).ConfigureAwait(false);
            if (picked is not null && picked.Value >= 0 && picked.Value < places.Count)
            {
                await _panels.GoAsync(side, places[picked.Value].Path).ConfigureAwait(false);
            }
        }
        finally
        {
            End();
        }
    }

    private List<Place> Places()
    {
        var free = _text.Get("drives.free");
        var places = new List<Place>();
        foreach (var drive in _drives.Mounted())
        {
            places.Add(new Place(DriveList.Line(drive, free), drive.Path));
        }

        var network = _text.Get("drives.share");
        foreach (var share in _shares.ListConnected())
        {
            if (!places.Exists(place => string.Equals(place.Path, share.Path, StringComparison.Ordinal)))
            {
                places.Add(new Place(share.Label + " (" + network + ")", share.Path));
            }
        }

        return places;
    }

    private bool Begin() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    private void End() => Interlocked.Exchange(ref _busy, 0);

    private sealed record Place(string Label, string Path);
}
