using System.Numerics;
using System.Text;
using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.MultiRename;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.MultiRename;

public sealed partial class RenameWindow
{
    private void DrawPreview()
    {
        PublishPreview();
        var rest = ImGui.GetContentRegionAvail().Y;
        var reserve = ImGui.GetFrameHeightWithSpacing() * 2;
        var min = FileListLayout.HeaderHeight + FileListLayout.RowHeight;
        _list.ViewportHeight = Math.Max(min, rest - reserve);
        _list.Draw();
    }

    private void PublishPreview()
    {
        var previews = _session.Previews;
        var stamp = Stamp(previews);
        if (_stamp is not null && string.Equals(stamp, _stamp, StringComparison.Ordinal))
        {
            return;
        }

        _stamp = stamp;
        var rows = new List<FileListRow>(previews.Count);
        for (var i = 0; i < previews.Count; i++)
        {
            rows.Add(Row(previews[i]));
        }

        _list.ShowRows(Columns(), rows);
    }

    private IReadOnlyList<FileListColumn> Columns()
    {
        var name = ImGui.GetFontSize() * 14;
        var error = ImGui.GetFontSize() * 8;
        return
        [
            new FileListColumn("rename.col.old", name),
            new FileListColumn("rename.col.new", name),
            new FileListColumn("error", error, star: true),
        ];
    }

    private static FileListRow Row(RenamePreview preview)
    {
        var oldName = NameOf(preview.From);
        var newName = NameOf(preview.To);
        var error = preview.Error ?? "";
        return new FileListRow(preview.From, [oldName, newName, error], oldName, false, false, false, preview);
    }

    private static string Stamp(IReadOnlyList<RenamePreview> previews)
    {
        var builder = new StringBuilder();
        foreach (var preview in previews)
        {
            builder.Append(preview.From);
            builder.Append('\n');
            builder.Append(preview.To);
            builder.Append('\n');
            builder.Append(preview.Error);
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string NameOf(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length == 0 ? path : name;
    }

    private void DrawActions()
    {
        var canStart = _session.CanStart && !IsBusy;
        CommandButton(_text.Get("start") + "##rename-start", canStart, BeginStart, out var start);
        StartCenter = start;
        StartEnabled = canStart;
        ImGui.SameLine();
        var canUndo = _session.CanUndo && !IsBusy;
        CommandButton(_text.Get("rename.undo") + "##rename-undo", canUndo, BeginUndo, out var undo);
        UndoCenter = undo;
        UndoEnabled = canUndo;
        DrawStatus();
    }

    private static void CommandButton(string label, bool enabled, Action invoke, out Vector2 center)
    {
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }

        var clicked = ImGui.Button(label);
        Vector2 min = default;
        Vector2 max = default;
        ImGui.GetItemRectMin(ref min);
        ImGui.GetItemRectMax(ref max);
        center = new Vector2((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f);
        if (!enabled)
        {
            ImGui.EndDisabled();
        }

        if (clicked && enabled)
        {
            invoke();
        }
    }

    private void DrawStatus()
    {
        var text = ReadStatus(out var error);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        ImGui.SameLine();
        if (error)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.86f, 0.28f, 0.24f, 1f));
        }

        ImGui.TextUnformatted(text);
        if (error)
        {
            ImGui.PopStyleColor();
        }
    }
}
