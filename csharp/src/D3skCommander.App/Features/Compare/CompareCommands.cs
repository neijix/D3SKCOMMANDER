using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Compare;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Compare;

/// <summary>Compare files opens the tool window. Shift+F2 marks differing files and reports the counts.</summary>
public sealed class CompareCommands
{
    private readonly PanelWorkspace _panels;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly IToolWindowHost _tools;
    private CompareView? _view;
    private string _left = "";
    private string _right = "";

    public CompareCommands(
        PanelWorkspace panels,
        IDialogService dialogs,
        ILocalizer text,
        IJobService jobs,
        IToolWindowHost tools)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(tools);
        _panels = panels;
        _dialogs = dialogs;
        _text = text;
        _jobs = jobs;
        _tools = tools;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.CompareFiles, CompareFiles);
        shell.Handle(CommandId.ComparePanels, () => _ = ComparePanelsAsync());
    }

    public CompareView CreateView()
    {
        _view = new CompareView(_left, _right, _jobs, _text, ReloadPanels);
        return _view;
    }

    public void CompareFiles()
    {
        var activeSide = _panels.ActiveSide;
        var otherSide = activeSide == PanelSide.Left ? PanelSide.Right : PanelSide.Left;
        if (!ComparePair.TryChoose(_panels.Read(activeSide), _panels.Read(otherSide), activeSide == PanelSide.Left, out var left, out var right))
        {
            _ = _dialogs.ShowMessageAsync(_text.Get("cmd.compare_files"), _text.Get("dlg.need_two_files"));
            return;
        }

        Show(left, right);
    }

    public async Task ComparePanelsAsync()
    {
        var leftSnap = _panels.Read(PanelSide.Left);
        var rightSnap = _panels.Read(PanelSide.Right);
        var leftSelection = new Selection();
        var rightSelection = new Selection();
        var counts = ComparePanels.Mark(leftSnap.Entries, leftSelection, rightSnap.Entries, rightSelection);
        _panels.ApplySelection(PanelSide.Left, leftSnap.Cursor, Chosen(leftSnap.Entries, leftSelection));
        _panels.ApplySelection(PanelSide.Right, rightSnap.Cursor, Chosen(rightSnap.Entries, rightSelection));
        await _dialogs.ShowMessageAsync(_text.Get("cmd.compare_dirs"), CompareText.Counts(_text, counts.Left, counts.Right))
            .ConfigureAwait(false);
    }

    private void Show(string left, string right)
    {
        _left = left;
        _right = right;
        if (_view is not null && _tools.IsOpen(ToolKind.Compare))
        {
            _view.Restart(left, right);
        }

        _tools.Open(ToolKind.Compare);
    }

    private void ReloadPanels() => _ = _panels.RefreshAsync();

    private static List<string> Chosen(IReadOnlyList<Entry> entries, Selection selection)
    {
        var paths = new List<string>();
        foreach (var entry in entries)
        {
            if (selection.Contains(entry.Path))
            {
                paths.Add(entry.Path);
            }
        }

        return paths;
    }
}
