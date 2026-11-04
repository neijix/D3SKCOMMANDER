using D3skCommander.Core.Localization;

namespace D3skCommander.App.Dialogs;

/// <summary>The platform folder chooser (zenity, kdialog, osascript, or the Windows shell dialog).</summary>
public sealed class OsFolderPicker : IFolderPicker
{
    private readonly IDialogOwner _owner;
    private readonly ILocalizer _localizer;

    public OsFolderPicker(IDialogOwner owner, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(localizer);
        _owner = owner;
        _localizer = localizer;
    }

    public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_owner.Current is null)
        {
            throw new InvalidOperationException("A folder picker needs the main window.");
        }

        var title = _localizer.Get("dialog.choose_folder");
        return Task.Run(() => NativeFolderDialog.Pick(title, startFolder, cancellationToken), cancellationToken);
    }
}
