using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
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
    [ObservableProperty]
    private bool _filterOpen;
    [ObservableProperty]
    private string _filterText = "";
    private bool _binding;

    public PanelPaneModel(PanelSide side, PanelWorkspace panels, ILocalizer text, IDialogService dialogs, Func<CommandId, bool>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        _side = side;
        _panels = panels;
        _text = text;
        _dialogs = dialogs;
        List = new FileList(text);
        List.RenameCommitted += OnRenameCommitted;
        _panels.RenameRequested += OnRenameRequested;
        InitOverlay();
        BrowseText = text.Get("panels.browse");
        FilterWatermark = text.Get("panels.filter");
        InitBar(dispatch);
    }

    public string? Name => _side == PanelSide.Left ? "Left" : "Right";

    public PanelSide Side => _side;

    public FileList List { get; }

    public string BrowseText { get; }

    public string FilterWatermark { get; }

    /// <summary>Raised when the quick-filter box opens, so the view can focus it.</summary>
    public event EventHandler? FilterOpened;

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

        PushFilter(snap);
        Refresh(_panels.Read(_side));
        ShowFailure(snap);
    }

    /// <summary>Esc in the filter box: the full list comes back.</summary>
    public void CloseFilter()
    {
        FilterOpen = false;
        _binding = true;
        FilterText = "";
        _binding = false;
        _panels.SetFilter(_side, null);
    }

    partial void OnFilterTextChanged(string value)
    {
        if (!_binding && FilterOpen)
        {
            _panels.SetFilter(_side, value);
        }
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
        var file = _panels.FileToOpen(_side);
        var open = _panels.OpenFile;
        if (file is not null && open is not null)
        {
            open(file);
            return Task.CompletedTask;
        }

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
        if (!ReferenceEquals(List.Entries, snap.Entries))
        {
            // A new list means new rows; the same list (only the cursor or the marks moved) keeps them.
            List.Entries = snap.Entries;
        }

        List.CursorIndex = snap.Cursor;
        List.SetMarks(snap.Marked);
        BindFilter(snap);
        if (!EditingPath)
        {
            Path = snap.Path;
        }
    }

    // The workspace opens the filter (Ctrl+S) or closes it (a new folder); the box follows.
    private void BindFilter(PanelSnapshot snap)
    {
        if (snap.Filter is null)
        {
            FilterOpen = false;
            return;
        }

        if (FilterOpen)
        {
            return;
        }

        _binding = true;
        FilterText = snap.Filter;
        _binding = false;
        FilterOpen = true;
        FilterOpened?.Invoke(this, EventArgs.Empty);
    }

    // A keystroke that arrived while a listing was busy is sent again on the next beat.
    private void PushFilter(PanelSnapshot snap)
    {
        if (FilterOpen && snap.Filter is not null && snap.Filter != FilterText)
        {
            _panels.SetFilter(_side, FilterText);
        }
    }

    private void ShowColumns()
    {
        if (_width <= 0)
        {
            return;
        }

        var flags = Flags(_panels.Columns.Fitting((float)_width, 8f));
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

    // Same count and every snapshot mark shown: the sets are equal. No copy: this runs every beat.
    private bool MarksDiffer(PanelSnapshot snap)
    {
        if (List.MarkedCount != snap.Marked.Count)
        {
            return true;
        }

        foreach (var mark in snap.Marked)
        {
            if (!List.IsMarked(mark))
            {
                return true;
            }
        }

        return false;
    }

    private void OnRenameRequested(object? sender, PanelSide side)
    {
        if (side == _side)
        {
            List.BeginRename();
        }
    }

    private void OnRenameCommitted(object? sender, FileListRenameEventArgs e)
    {
        if (e.Row.Source is Entry entry && !entry.IsParent && entry.Name != e.Name)
        {
            _panels.CommitRename(entry.Path, e.Name);
        }
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
