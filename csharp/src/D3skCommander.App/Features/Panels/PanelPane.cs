using System.Globalization;
using System.Numerics;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.Panels;

/// <summary>Path bar, file list and selection count for one panel.</summary>
public sealed class PanelPane : IShellWidget
{
    private readonly PanelSide _side;
    private readonly PanelWorkspace _panels;
    private readonly ILocalizer _text;
    private readonly IDialogService _dialogs;
    private readonly FileList _list = new();
    private readonly List<string> _marks = [];
    private string _path = "";
    private bool _pathActive;
    private bool _pathCommitted;
    private bool _kicked;
    private int _generation = -1;
    private int _seenFailure = -1;
    private float _listTop;

    public PanelPane(PanelSide side, PanelWorkspace panels, ILocalizer text, IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        _side = side;
        _panels = panels;
        _text = text;
        _dialogs = dialogs;
        _list.Use(text);
    }

    public string? Name => _side == PanelSide.Left ? "Left" : "Right";

    public void Draw()
    {
        if (!_kicked)
        {
            _kicked = true;
            _panels.KickOpen();
        }

        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _panels.Activate(_side);
        }

        var snap = _panels.Read(_side);
        if (snap.Generation != _generation)
        {
            Bind(snap);
            _generation = snap.Generation;
        }

        DrawPath(snap);
        DrawList();
        var open = WantsOpen();
        HandleMovement();
        if (snap.Generation == _generation)
        {
            PushSelection(snap);
        }

        if (open)
        {
            _ = _panels.EnterAsync();
        }

        DrawStatus(snap);
        ShowFailure(snap);
        _panels.NoteFrame();
    }

    private void Bind(PanelSnapshot snap)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var flags = Fit(Columns.Default.Fitting(width, 8f));
        if (_list.ShownColumns != flags)
        {
            _list.ShownColumns = flags;
        }

        if (_list.View != snap.View)
        {
            _list.View = snap.View;
        }

        _list.Entries = snap.Entries;
        _list.CursorIndex = snap.Cursor;
        _list.SetMarks(snap.Marked);
        if (!_pathActive)
        {
            _path = snap.Path;
        }
    }

    private void DrawPath(PanelSnapshot snap)
    {
        _pathCommitted = false;
        if (!_pathActive && _path != snap.Path)
        {
            _path = snap.Path;
        }

        var browse = _text.Get("panels.browse");
        var button = ImGui.CalcTextSize(browse).X + 16;
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(Math.Max(40, width - button - 8));
        ImGui.InputText("##path-" + _side, ref _path, 4096);
        var entered = ImGui.IsItemDeactivated() && ImGui.IsKeyPressed(ImGuiKey.Enter);
        _pathActive = ImGui.IsItemActive();
        ImGui.SameLine();
        if (ImGui.Button(browse + "##browse-" + _side))
        {
            _ = _panels.BrowseAsync(_side);
        }

        if (entered)
        {
            _pathCommitted = true;
            _ = _panels.GoAsync(_side, _path);
        }
    }

    private void DrawList()
    {
        var rest = ImGui.GetContentRegionAvail().Y;
        _list.ViewportHeight = Math.Max(FileListLayout.HeaderHeight + FileListLayout.RowHeight, rest - 22);
        _listTop = ImGui.GetCursorScreenPos().Y;
        _list.Draw();
    }

    private void DrawStatus(PanelSnapshot snap)
    {
        var line = _text.Get("panels.selected")
            .Replace("{0}", snap.Selected.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{1}", snap.Selectable.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (_panels.ActiveSide == _side)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.82f, 1f, 1f));
        }

        ImGui.TextUnformatted(line);
        if (_panels.ActiveSide == _side)
        {
            ImGui.PopStyleColor();
        }
    }

    private bool WantsOpen()
    {
        if (_pathCommitted || _panels.ActiveSide != _side || _list.IsRenaming)
        {
            return false;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Enter) && !ImGui.GetIO().WantTextInput)
        {
            return true;
        }

        return ImGui.IsWindowHovered()
            && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)
            && ImGui.GetIO().MousePos.Y >= _listTop;
    }

    private void HandleMovement()
    {
        if (_panels.ActiveSide != _side || ImGui.GetIO().WantTextInput || _list.IsRenaming)
        {
            return;
        }

        if (ImGui.GetIO().KeyCtrl || ImGui.GetIO().KeySuper)
        {
            return;
        }

        var modifiers = ImGui.GetIO().KeyShift ? FileListModifiers.Shift : FileListModifiers.None;
        Press(ImGuiKey.UpArrow, FileListKey.Up, modifiers);
        Press(ImGuiKey.DownArrow, FileListKey.Down, modifiers);
        Press(ImGuiKey.PageUp, FileListKey.PageUp, modifiers);
        Press(ImGuiKey.PageDown, FileListKey.PageDown, modifiers);
        Press(ImGuiKey.Home, FileListKey.Home, modifiers);
        Press(ImGuiKey.End, FileListKey.End, modifiers);
        Press(ImGuiKey.Insert, FileListKey.Insert, modifiers);
        Press(ImGuiKey.Space, FileListKey.Space, modifiers);
    }

    private void Press(ImGuiKey imgui, FileListKey key, FileListModifiers modifiers)
    {
        if (ImGui.IsKeyPressed(imgui))
        {
            _list.Key(key, modifiers);
        }
    }

    private void PushSelection(PanelSnapshot snap)
    {
        if (_list.CursorIndex == snap.Cursor && !MarksDiffer(snap))
        {
            return;
        }

        _list.CopyMarks(_marks);
        _panels.ApplySelection(_side, _list.CursorIndex, _marks);
    }

    private bool MarksDiffer(PanelSnapshot snap)
    {
        _list.CopyMarks(_marks);
        if (_marks.Count != snap.Marked.Count)
        {
            return true;
        }

        foreach (var mark in _marks)
        {
            var found = false;
            foreach (var existing in snap.Marked)
            {
                if (existing == mark)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
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

    private static FileListColumnFlags Fit(Columns columns)
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
