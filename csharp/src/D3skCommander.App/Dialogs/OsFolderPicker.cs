using Avalonia.Platform.Storage;
using Avalonia.Threading;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Dialogs;

/// <summary>The platform folder chooser (<see cref="IStorageProvider.OpenFolderPickerAsync"/>).</summary>
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            return PickOnUiAsync(startFolder, cancellationToken);
        }

        return Dispatcher.UIThread.InvokeAsync(() => PickOnUiAsync(startFolder, cancellationToken));
    }

    private async Task<string?> PickOnUiAsync(string? startFolder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = _owner.Current ?? throw new InvalidOperationException("A folder picker needs the main window.");
        var options = new FolderPickerOpenOptions
        {
            Title = _localizer.Get("dialog.choose_folder"),
            AllowMultiple = false,
        };
        await ApplyStartAsync(owner.StorageProvider, options, startFolder);
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (folders.Count == 0)
        {
            return null;
        }

        return folders[0].TryGetLocalPath();
    }

    private static async Task ApplyStartAsync(IStorageProvider storage, FolderPickerOpenOptions options, string? startFolder)
    {
        if (string.IsNullOrWhiteSpace(startFolder))
        {
            return;
        }

        var folder = await storage.TryGetFolderFromPathAsync(startFolder);
        if (folder is not null)
        {
            options.SuggestedStartLocation = folder;
        }
    }
}
