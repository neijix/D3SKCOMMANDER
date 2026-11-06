using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Drives;

/// <summary>The menu as a modal list: used when no drive button can anchor a drop-down.</summary>
public sealed class DialogDriveMenuPresenter : IDriveMenuPresenter
{
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;

    public DialogDriveMenuPresenter(IDialogService dialogs, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        _dialogs = dialogs;
        _text = text;
    }

    public async Task<DriveMenuItem?> ShowAsync(PanelSide side, IReadOnlyList<DriveMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var title = _text.Get(side == PanelSide.Left ? "cmd.drives_left" : "cmd.drives_right");
        var picked = await _dialogs.ChooseAsync(title, _text.Get("drives.choose"), items.Select(item => item.Text).ToList()).ConfigureAwait(false);
        return picked is { } index && index >= 0 && index < items.Count ? items[index] : null;
    }
}
