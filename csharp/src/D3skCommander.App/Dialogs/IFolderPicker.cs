namespace D3skCommander.App.Dialogs;

/// <summary>The OS folder chooser. Headless tests substitute a fake that answers without one.</summary>
public interface IFolderPicker
{
    /// <summary>The chosen folder, or null when the user cancels.</summary>
    Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default);
}
