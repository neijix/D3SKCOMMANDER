using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

/// <summary>
/// The rules of one panel pane: path bar, file list and selection count. The view calls
/// <see cref="Tick"/> on a timer, so the poll and new listings reach the screen without input.
/// </summary>
public sealed partial class PanelPaneModel : ObservableObject, IShellWidget
{
    private readonly PanelSide _side;
    private readonly PanelWorkspace _panels;
    private readonly ILocalizer _text;
    private readonly IDialogService _dialogs;
    private readonly List<string> _marks = [];
    private bool _shown;
    private double _width;
    private int _generation = -1;
    private int _seenFailure = -1;
    [ObservableProperty]
    private string _path = "";
    [ObservableProperty]
    private string _statusText = "";
    [ObservableProperty]
    private bool _isActive;

    public PanelPaneModel(PanelSide side, PanelWorkspace panels, ILocalizer text, IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        _side = side;
        _panels = panels;
        _text = text;
        _dialogs = dialogs;
        List = new FileList(text);
        BrowseText = text.Get("panels.browse");
    }

    public string? Name => _side == PanelSide.Left ? "Left" : "Right";

    public PanelSide Side => _side;

    public FileList List { get; }

    public string BrowseText { get; }

    /// <summary>True while the path box has the focus: a new listing then leaves the typed text alone.</summary>
    public bool EditingPath { get; set; }

    public Control CreateContent() => new PanelPaneView(this);

    /// <summary>The pane appeared: the first show starts the initial listing.</summary>
    public void Show()
    {
        if (_shown)
        {
            return;
        }

        _shown = true;
        _panels.KickOpen();
        Tick();
    }

    public void Activate()
    {
        _panels.Activate(_side);
        IsActive = true;
    }

    /// <summary>Fits the columns to the pane width in device-independent pixels.</summary>
    public void Fit(double width)
    {
        _width = width;
        ShowColumns();
    }

    /// <summary>One beat of the pane: the poll, a new listing, the marks, the status and a folder failure.</summary>
    public void Tick()
    {
        _panels.PollWhenDue();
        var snap = _panels.Read(_side);
        if (snap.Generation != _generation)
        {
            Bind(snap);
            _generation = snap.Generation;
        }
        else
        {
            PushSelection(snap);
        }

        Refresh(_panels.Read(_side));
        ShowFailure(snap);
    }

    /// <summary>Pushes what the list shows (cursor and marks) to the workspace, after input.</summary>
    public void Sync()
    {
        var snap = _panels.Read(_side);
        if (snap.Generation == _generation)
        {
            PushSelection(snap);
        }

        Refresh(_panels.Read(_side));
    }

    /// <summary>Enter in the path bar: go to the typed path.</summary>
    public Task CommitPathAsync() => _panels.GoAsync(_side, Path);

    public Task BrowseAsync() => _panels.BrowseAsync(_side);

    /// <summary>Opens the entry under the cursor, only on the active side and outside a rename.</summary>
    public Task OpenEntryAsync()
    {
        if (_panels.ActiveSide != _side || List.IsRenaming)
        {
            return Task.CompletedTask;
        }

        Sync();
        return _panels.EnterAsync();
    }

    /// <summary>Whether the list takes a key: only the active side, and never with Ctrl or Super held.</summary>
    public bool AcceptsKey(bool control, bool super) => _panels.ActiveSide == _side && !control && !super;

    private void Bind(PanelSnapshot snap)
    {
        if (List.View != snap.View)
        {
            List.View = snap.View;
        }

        ShowColumns();
        List.Entries = snap.Entries;
        List.CursorIndex = snap.Cursor;
        List.SetMarks(snap.Marked);
        if (!EditingPath)
        {
            Path = snap.Path;
        }
    }

    private void ShowColumns()
    {
        if (_width <= 0)
        {
            return;
        }

        var flags = Flags(Columns.Default.Fitting((float)_width, 8f));
        if (List.ShownColumns != flags)
        {
            List.ShownColumns = flags;
        }
    }

    private void Refresh(PanelSnapshot snap)
    {
        IsActive = _panels.ActiveSide == _side;
        StatusText = _text.Get("panels.selected")
            .Replace("{0}", snap.Selected.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{1}", snap.Selectable.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (!EditingPath && Path != snap.Path)
        {
            Path = snap.Path;
        }
    }

    private void PushSelection(PanelSnapshot snap)
    {
        if (List.CursorIndex == snap.Cursor && !MarksDiffer(snap))
        {
            return;
        }

        List.CopyMarks(_marks);
        _panels.ApplySelection(_side, List.CursorIndex, _marks);
    }

    private bool MarksDiffer(PanelSnapshot snap)
    {
        List.CopyMarks(_marks);
        if (_marks.Count != snap.Marked.Count)
        {
            return true;
        }

        var known = new HashSet<string>(snap.Marked, StringComparer.Ordinal);
        foreach (var mark in _marks)
        {
            if (!known.Contains(mark))
            {
                return true;
            }
        }

        return false;
    }

    private void ShowFailure(PanelSnapshot snap)
    {
        if (snap.Error is null || snap.FailureToken == _seenFailure)
        {
            return;
        }

        _seenFailure = snap.FailureToken;
        var message = _text.Get(ErrorText.Key(snap.Error.Value));
        if (!string.IsNullOrEmpty(snap.ErrorPath))
        {
            message = message + "\n" + snap.ErrorPath;
        }

        _ = _dialogs.ShowMessageAsync(_text.Get("panels.folder"), message);
    }

    private static FileListColumnFlags Flags(Columns columns)
    {
        var flags = FileListColumnFlags.None;
        if (columns.Extension)
        {
            flags |= FileListColumnFlags.Extension;
        }

        if (columns.Size)
        {
            flags |= FileListColumnFlags.Size;
        }

        if (columns.Date)
        {
            flags |= FileListColumnFlags.Date;
        }

        if (columns.Attributes)
        {
            flags |= FileListColumnFlags.Attributes;
        }

        return flags;
    }
}
