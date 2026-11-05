using Avalonia.Platform.Storage;
using D3skCommander.App.Host;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Dialogs;

/// <summary>The platform folder chooser (<see cref="IStorageProvider.OpenFolderPickerAsync"/>) of the owner window.</summary>
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
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        UiThread.Post(() => _ = PickOnUiAsync(startFolder, cancellationToken, done));
        return done.Task;
    }

    private async Task PickOnUiAsync(string? startFolder, CancellationToken cancellationToken, TaskCompletionSource<string?> done)
    {
        try
        {
            var owner = _owner.Current ?? throw new InvalidOperationException("A folder picker needs the main window.");
            var options = new FolderPickerOpenOptions
            {
                Title = _localizer.Get("dialog.choose_folder"),
                AllowMultiple = false,
            };
            await ApplyStartAsync(owner.StorageProvider, options, startFolder);
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(options);
            cancellationToken.ThrowIfCancellationRequested();
            done.TrySetResult(folders.Count == 0 ? null : folders[0].TryGetLocalPath());
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
        }
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
