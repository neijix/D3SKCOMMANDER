using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Drives;

public sealed partial class DrivesCommands
{
    /// <summary>Opens the drive menu of <paramref name="side"/> and does what the chosen line says.</summary>
    public async Task ChooseDriveAsync(PanelSide side)
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            var current = _panels.Read(side).Path;
            var items = await Task.Run(() => LoadItems(current)).ConfigureAwait(false);
            if (items.Count == 0)
            {
                await _dialogs.ShowMessageAsync(_text.Get(side == PanelSide.Left ? "cmd.drives_left" : "cmd.drives_right"), _text.Get("drives.none")).ConfigureAwait(false);
                return;
            }

            var picked = await _menu.ShowAsync(side, items).ConfigureAwait(false);
            if (picked is not null)
            {
                await ActAsync(side, picked).ConfigureAwait(false);
            }
        }
        finally
        {
            End();
        }
    }

    private List<DriveMenuItem> LoadItems(string current)
    {
        var devices = ReadDevices();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var places = UserPlaces.Standard(home, UserPlaces.ReadUserDirs(home), Directory.Exists);
        return DriveMenu.Build(_text, current, _drives.Mounted(), places, _shares.ListConnected(), devices);
    }

    private IReadOnlyList<BlockDevice> ReadDevices()
    {
        var json = _devices.List();
        return json is null ? [] : BlockDevices.ParseLsblk(json);
    }

    private async Task ActAsync(PanelSide side, DriveMenuItem item)
    {
        switch (item.Kind)
        {
            case DriveMenuKind.Computer:
                // The method the Computer command runs, for this menu's side.
                await _panels.ComputerAsync(side).ConfigureAwait(false);
                break;
            case DriveMenuKind.Unmounted:
                await MountAsync(side, item.Target).ConfigureAwait(false);
                break;
            case DriveMenuKind.Eject:
                await EjectAsync(item.Target).ConfigureAwait(false);
                break;
            default:
                await _panels.GoAsync(side, item.Target).ConfigureAwait(false);
                break;
        }
    }

    private async Task MountAsync(PanelSide side, string device)
    {
        var result = await Task.Run(() => _devices.Mount(device)).ConfigureAwait(false);
        var path = result.Ok ? Udisks.ParseMounted(result.Stdout) : null;
        if (path is null)
        {
            await Failed("drives.mount_failed", result).ConfigureAwait(false);
            return;
        }

        await _panels.GoAsync(side, path).ConfigureAwait(false);
    }

    private async Task EjectAsync(string mountPath)
    {
        var json = await Task.Run(ReadDevices).ConfigureAwait(false);
        if (BlockDevices.EjectTargets(json, mountPath) is not { } target)
        {
            return;
        }

        var result = await Task.Run(() => _devices.Unmount(target.Partition)).ConfigureAwait(false);
        if (result.Ok)
        {
            await LeaveAsync(mountPath).ConfigureAwait(false);
            result = await Task.Run(() => _devices.PowerOff(target.Disk)).ConfigureAwait(false);
        }

        if (!result.Ok)
        {
            await Failed("drives.eject_failed", result).ConfigureAwait(false);
        }
    }

    // Both panels that sit inside the ejected volume go Home.
    private async Task LeaveAsync(string mountPath)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var side in (PanelSide[])[PanelSide.Left, PanelSide.Right])
        {
            var path = _panels.Read(side).Path;
            var root = mountPath.TrimEnd('/');
            if (path == root || path.StartsWith(root + "/", StringComparison.Ordinal))
            {
                await _panels.GoAsync(side, home).ConfigureAwait(false);
            }
        }
    }

    private Task Failed(string titleKey, ToolResult result) =>
        _dialogs.ShowMessageAsync(_text.Get(titleKey), result.Stderr.Length > 0 ? result.Stderr : _text.Get("drives.tool_failed"));
}
