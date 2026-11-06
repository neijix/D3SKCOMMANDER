using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Associations;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Associations;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Editor;

/// <summary>
/// Edit (F4) opens the first file of the active panel's selection in the editor window; folders
/// are skipped. EditNew (Shift+F4) asks for a name in the active folder. One window serves both.
/// </summary>
public sealed class EditorCommands(IPanelWorkspace panels, IToolWindowHost tools, IDialogService dialogs, ILocalizer text, IFileAssociations? associations = null)
{
    private readonly IPanelWorkspace _panels = panels ?? throw new ArgumentNullException(nameof(panels));
    private readonly IToolWindowHost _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    private readonly IDialogService _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    private readonly ILocalizer _text = text ?? throw new ArgumentNullException(nameof(text));
    private readonly IFileAssociations? _associations = associations;

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Edit, () => _ = EditAsync());
        shell.Handle(CommandId.EditNew, () => _ = EditNewAsync());
        _tools.Register(ToolKind.Editor, () => new EditorViewModel(_text, _dialogs, () => _panels.RefreshAsync()));
    }

    /// <summary>Opens the window, then loads the file; the task ends when the text is shown.</summary>
    public Task EditAsync()
    {
        var capture = _panels.Capture();
        if (capture is null)
        {
            return Task.CompletedTask;
        }

        var path = capture.Sources.FirstOrDefault(source => !Directory.Exists(source));
        if (path is null)
        {
            return _dialogs.ShowMessageAsync(_text.Get("cmd.edit"), _text.Get("dlg.no_selection"));
        }

        return _associations?.TryStart(AssociationKind.Edit, path) ?? OpenAsync(path);
    }

    public async Task EditNewAsync()
    {
        var capture = _panels.Capture();
        if (capture is null)
        {
            return;
        }

        var title = _text.Get("cmd.edit_new");
        var name = (await _dialogs.AskTextAsync(title, _text.Get("editor.new_name")).ConfigureAwait(true))?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (LocalFileSystem.ValidateName(name) is { } invalid)
        {
            await _dialogs.ShowMessageAsync(title, _text.Get(ErrorText.Key(invalid.Kind)) + ": " + name).ConfigureAwait(true);
            return;
        }

        await OpenAsync(Path.Combine(capture.ActiveDirectory, name)).ConfigureAwait(true);
    }

    /// <summary>Shows <paramref name="path"/>; a dirty buffer is settled first, and a refusal keeps it.</summary>
    private async Task OpenAsync(string path)
    {
        var window = _tools.Open(ToolKind.Editor);
        if (window.View is not EditorViewModel model || !await model.CanCloseAsync().ConfigureAwait(true))
        {
            return;
        }

        await model.OpenAsync(path).ConfigureAwait(true);
    }
}
