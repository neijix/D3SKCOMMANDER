using D3skCommander.App.Features.Drives;
using D3skCommander.App.Shell;

namespace D3skCommander.App.Tests.Features.Drives;

/// <summary>A scripted <c>lsblk</c>/<c>udisksctl</c>: records every call, never touches a disk.</summary>
internal sealed class FakeBlockDevices(string mountedAt) : IBlockDeviceRunner
{
    public string? Json { get; set; }

    public bool MountOk { get; set; } = true;

    public List<string> Calls { get; } = [];

    public string? List() => Json;

    public ToolResult Mount(string device)
    {
        Calls.Add("mount " + device);
        return MountOk
            ? new ToolResult(true, "Mounted " + device + " at " + mountedAt + ".\n", "")
            : new ToolResult(false, "", "Error mounting " + device + ": busy");
    }

    public ToolResult Unmount(string device)
    {
        Calls.Add("unmount " + device);
        return new ToolResult(true, "", "");
    }

    public ToolResult PowerOff(string device)
    {
        Calls.Add("poweroff " + device);
        return new ToolResult(true, "", "");
    }
}

/// <summary>Records the items shown and picks the first whose kind matches (null: dismiss).</summary>
internal sealed class FakeMenu : IDriveMenuPresenter
{
    public DriveMenuKind? Pick { get; set; }

    public IReadOnlyList<DriveMenuItem> Shown { get; private set; } = [];

    public Task<DriveMenuItem?> ShowAsync(PanelSide side, IReadOnlyList<DriveMenuItem> items)
    {
        Shown = items;
        return Task.FromResult(Pick is null ? null : items.FirstOrDefault(item => item.Kind == Pick));
    }
}
