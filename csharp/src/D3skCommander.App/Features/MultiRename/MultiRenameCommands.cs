using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.MultiRename;

/// <summary>Ctrl+M opens the multi-rename tool on the active panel's selection.</summary>
public sealed class MultiRenameCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IToolWindowHost _tools;
    private readonly RenameWindow _window;

    public MultiRenameCommands(
        IPanelWorkspace panels,
        IDialogService dialogs,
        ILocalizer text,
        IToolWindowHost tools,
        RenameWindow window)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(window);
        _panels = panels;
        _dialogs = dialogs;
        _text = text;
        _tools = tools;
        _window = window;
        _window.BatchChanged += RefreshPanels;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.MultiRename, Open);
        _tools.Register(ToolKind.MultiRename, () => _window);
    }

    private void Open()
    {
        var capture = _panels.Capture();
        if (capture is null || capture.Sources.Count == 0)
        {
            _ = _dialogs.ShowMessageAsync(_text.Get("cmd.multirename"), _text.Get("dlg.no_selection"));
            return;
        }

        _window.Load(capture.Sources);
        _tools.Open(ToolKind.MultiRename);
    }

    private void RefreshPanels() => _ = RefreshPanelsAsync();

    private async Task RefreshPanelsAsync()
    {
        try
        {
            await _panels.RefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // The panels report a folder they cannot list. The rename already finished.
        }
    }
}
