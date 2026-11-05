using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Catalog;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Catalog;

/// <summary>The Catalog command. The output path is the same text prompt as v0.1; the job writes the <c>.d3cat</c>.</summary>
public sealed class CatalogCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly CatalogDialog _dialog;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IDialogOwner _owner;
    private CatalogWindow? _window;

    public CatalogCommands(IPanelWorkspace panels, CatalogDialog dialog, IDialogService dialogs, ILocalizer text, IDialogOwner owner)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        _panels = panels;
        _dialog = dialog;
        _dialogs = dialogs;
        _text = text;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Catalog, () => _ = RunAsync());
    }

    public async Task RunAsync()
    {
        if (!_dialog.TryBegin())
        {
            return;
        }

        try
        {
            var capture = _panels.Capture();
            if (capture is null)
            {
                return;
            }

            var output = await AskOutput(capture).ConfigureAwait(false);
            if (output is null)
            {
                return;
            }

            _dialog.Prepare(capture.ActiveDirectory, output);
            if (await _dialog.WriteAsync().ConfigureAwait(false))
            {
                UiThread.Post(ShowWindow);
                await _panels.RefreshAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _dialog.End();
        }
    }

    /// <summary>Opens the catalog window over the main window; a window already open stays.</summary>
    private void ShowWindow()
    {
        if (_window is { IsVisible: true } || _owner.Current is not { } main)
        {
            return;
        }

        _window = new CatalogWindow(_dialog, _text);
        _ = _window.ShowDialog(main);
    }

    private async Task<string?> AskOutput(PanelCapture capture)
    {
        var suggested = Suggest(capture.ActiveDirectory, capture.OtherDirectory);
        var output = await _dialogs.AskTextAsync(
            _text.Get("cmd.catalog"),
            _text.Get("dlg.out_file"),
            suggested).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        return output.Trim();
    }

    private static string Suggest(string root, string other)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        if (name.Length == 0)
        {
            name = "disk";
        }

        return Path.Combine(other, name + DiskCatalog.FileExtension);
    }
}
