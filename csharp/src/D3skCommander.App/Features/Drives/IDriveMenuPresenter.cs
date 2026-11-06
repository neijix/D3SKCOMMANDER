using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Drives;

/// <summary>Shows the drive menu of one panel and answers the line chosen, or null when it is dismissed.</summary>
public interface IDriveMenuPresenter
{
    Task<DriveMenuItem?> ShowAsync(PanelSide side, IReadOnlyList<DriveMenuItem> items);
}
