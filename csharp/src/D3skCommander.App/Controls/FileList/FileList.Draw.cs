using System.Numerics;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Controls.FileList;

public sealed partial class FileList
{
    public void Draw()
    {
        if (_columns.Count == 0)
        {
            return;
        }

        var flags = ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("filelist", _columns.Count, flags, new Vector2(0, (float)ViewportHeight)))
        {
            return;
        }

        SetupColumns();
        ImGui.TableHeadersRow();
        var end = Math.Min(_rowList.Count, _scroll + VisibleRows());
        for (var i = _scroll; i < end; i++)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, (float)FileListLayout.RowHeight);
            DrawRow(i);
        }

        ImGui.EndTable();
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
        var alpha = row.IsHidden ? 0.5f : 1f;
        if (row.IsCursor)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(new Vector4(0.73f, 0.84f, 1f, alpha)));
        }

        for (var column = 0; column < _columns.Count; column++)
        {
            ImGui.TableSetColumnIndex(column);
            if (column == 0 && row.IsEditing)
            {
                DrawEditor(row);
                continue;
            }

            var text = column < row.Cells.Count ? row.Cells[column] : "";
            if (row.IsMarked)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.78f, 0.12f, 0.08f, alpha));
            }
            else if (alpha < 1)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 1, alpha));
            }

            ImGui.TextUnformatted(text);
            if (row.IsMarked || alpha < 1)
            {
                ImGui.PopStyleColor();
            }

            if (column == 0)
            {
                NotePress(index);
            }
        }
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
        if (!ImGui.IsItemHovered() || !ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            return;
        }

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
}
