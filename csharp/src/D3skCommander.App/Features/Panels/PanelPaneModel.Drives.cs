using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Features.Drives;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneModel
{
    private DriveBarModel? _driveBar;

    [ObservableProperty]
    private bool _showDriveBar = true;
    [ObservableProperty]
    private IReadOnlyList<DriveButton> _driveButtons = [];

    /// <summary>Left click on a drive button: this panel goes to the drive's root.</summary>
    public void GoToDrive(string path)
    {
        Activate();
        _ = _panels.GoAsync(_side, path);
    }

    /// <summary>Middle click on a drive button: the drive's root opens in a new tab of this panel.</summary>
    public void OpenDriveInNewTab(string path)
    {
        Activate();
        _ = _panels.NewTabAtAsync(_side, path);
    }

    private void InitDrives(IDriveSource? drives, ILocalizer text)
    {
        _driveBar = drives is null ? null : new DriveBarModel(drives, text);
    }

    private void TickDrives(string path)
    {
        ShowDriveBar = _panels.ShowDriveBar;
        if (ShowDriveBar && _driveBar is { } bar && bar.Update(path, _panels.Favorites))
        {
            DriveButtons = bar.Buttons;
        }
    }
}
