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
    private readonly IBlockDeviceRunner _devices;
    private readonly IDriveMenuPresenter _menu;
    private int _busy;

    public DrivesCommands(
        IPanelWorkspace panels,
        IDialogService dialogs,
        ILocalizer text,
        IJobService jobs,
        IDriveSource drives,
        INetDriveRunner shares,
        FileOperationService files,
        IFileSystemProvider provider,
        IBlockDeviceRunner devices,
        IDriveMenuPresenter menu)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(menu);
        _panels = panels;
        _dialogs = dialogs;
        _text = text;
        _jobs = jobs;
        _drives = drives;
        _shares = shares;
        _files = files;
        _provider = provider;
        _devices = devices;
        _menu = menu;
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

    private bool Begin() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    private void End() => Interlocked.Exchange(ref _busy, 0);
}
