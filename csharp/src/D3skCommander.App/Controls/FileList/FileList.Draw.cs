using System.Numerics;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Controls.FileList;

public sealed partial class FileList
{
    public void Draw()
    {
        _activated = -1;
        _rowTops.Clear();
        if (_columns.Count == 0)
        {
            return;
        }

        var flags = ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV
            | ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("filelist", _columns.Count, flags, new Vector2(0, (float)ViewportHeight)))
        {
            return;
        }

        SetupColumns();
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        DrawBody();
        ApplyPendingScroll();
        if (_scrollRequest < 0)
        {
            _scroll = ScrollFromPixels(ImGui.GetScrollY());
        }

        ImGui.EndTable();
    }

    private void DrawBody()
    {
        var clipper = ImGui.ImGuiListClipper();
        try
        {
            ImGui.Begin(clipper, _rowList.Count, (float)FileListLayout.RowHeight);
            while (ImGui.Step(clipper))
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    DrawRow(i);
                }
            }
        }
        finally
        {
            ImGui.Destroy(clipper);
        }
    }

    private void ApplyPendingScroll()
    {
        if (_scrollRequest < 0)
        {
            return;
        }

        var y = _scrollRequest * (float)FileListLayout.RowHeight;
        ImGui.SetScrollY(y);
        var actual = ImGui.GetScrollY();
        var max = ImGui.GetScrollMaxY();
        var reached = Math.Abs(actual - y) <= 1f || (max > 0 && y >= max && actual >= max - 1f);
        if (reached)
        {
            _scrollRequest = -1;
        }
    }

    private void SetupColumns()
    {
        foreach (var column in _columns)
        {
            var tableFlags = column.Star ? ImGuiTableColumnFlags.WidthStretch : ImGuiTableColumnFlags.WidthFixed;
            ImGui.TableSetupColumn(_localizer.Get(column.HeaderKey), tableFlags, (float)column.Width);
        }
    }

    private void DrawRow(int index)
    {
        var row = _rowList[index];
        ImGui.TableNextRow(ImGuiTableRowFlags.None, (float)FileListLayout.RowHeight);
        ImGui.TableSetColumnIndex(0);
        HitRow(index, row);
        PaintCells(row);
    }

    private void HitRow(int index, FileListRow row)
    {
        ImGui.PushID(index);
        var flags = ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick;
        if (row.IsEditing)
        {
            flags |= ImGuiSelectableFlags.AllowOverlap;
        }
        var alpha = row.IsHidden ? 0.5f : 1f;
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.73f, 0.84f, 1f, alpha));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.62f, 0.78f, 1f, alpha));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.50f, 0.70f, 0.98f, alpha));
        ImGui.Selectable("##row", row.IsCursor, flags, new Vector2(0, (float)FileListLayout.RowHeight));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        _rowTops[index] = min.Y;
        _rowHitX = max.X - 8;
        var hovered = ImGui.IsItemHovered();
        ImGui.PopStyleColor(3);
        ImGui.PopID();
        if (row.IsEditing || !hovered)
        {
            return;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            NotePress(index);
        }

        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _activated = index;
        }
    }

    private void PaintCells(FileListRow row)
    {
        var color = ImGui.GetColorU32(TextColor(row));
        for (var column = 0; column < _columns.Count; column++)
        {
            ImGui.TableSetColumnIndex(column);
            if (column == 0 && row.IsEditing)
            {
                DrawEditor(row);
                continue;
            }

            var text = column < row.Cells.Count ? row.Cells[column] : "";
            DrawClipped(text, color);
        }
    }

    private static void DrawClipped(string text, uint color)
    {
        var min = ImGui.GetCursorScreenPos();
        var width = Math.Max(0, ImGui.GetContentRegionAvail().X);
        var max = new Vector2(min.X + width, min.Y + (float)FileListLayout.RowHeight);
        ImGui.PushClipRect(min, max, true);
        ImGui.GetWindowDrawList().AddText(min, color, text);
        ImGui.PopClipRect();
    }

    private static Vector4 TextColor(FileListRow row)
    {
        var alpha = row.IsHidden ? 0.5f : 1f;
        if (row.IsMarked)
        {
            return new Vector4(0.78f, 0.12f, 0.08f, alpha);
        }

        return new Vector4(1, 1, 1, alpha);
    }

    private void DrawEditor(FileListRow row)
    {
        var buffer = row.EditBuffer;
        if (ImGui.InputText("##rename", ref buffer, (nuint)1024))
        {
            row.EditBuffer = buffer;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            CommitRename();
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            CancelRename();
        }
    }

    private void NotePress(int index)
    {
        var io = ImGui.GetIO();
        var modifiers = FileListModifiers.None;
        if (io.KeyShift)
        {
            modifiers |= FileListModifiers.Shift;
        }

        if (io.KeyCtrl || io.KeySuper)
        {
            modifiers |= FileListModifiers.Toggle;
        }

        PointerDown(index, modifiers);
    }

    private int ScrollFromPixels(float y)
    {
        var row = (int)Math.Floor(y / (float)FileListLayout.RowHeight);
        if (row < 0 || _rowList.Count == 0)
        {
            return 0;
        }

        return Math.Min(row, _rowList.Count - 1);
    }
}
