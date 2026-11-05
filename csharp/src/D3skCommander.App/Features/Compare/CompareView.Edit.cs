using System.Numerics;
using D3skCommander.Core.Compare;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.Compare;

public sealed partial class CompareView
{
    private void DrawEditors()
    {
        var left = _session.LeftBuffer;
        var right = _session.RightBuffer;
        if (left is null || right is null)
        {
            SurfaceText = "";
            return;
        }

        SurfaceText = left.Path;
        var height = Math.Max(ImGui.GetTextLineHeightWithSpacing() * 6, ImGui.GetContentRegionAvail().Y);
        if (!ImGui.BeginTable("compare-edit", 2, ImGuiTableFlags.BordersInnerV, new Vector2(0, height)))
        {
            return;
        }

        ImGui.TableNextColumn();
        DrawEditor(0, left, ref _leftEdit, ref _leftSource);
        ImGui.TableNextColumn();
        DrawEditor(1, right, ref _rightEdit, ref _rightSource);
        ImGui.EndTable();
    }

    private void DrawEditor(int side, CompareBuffer buffer, ref string text, ref CompareBuffer? source)
    {
        if (!ReferenceEquals(source, buffer))
        {
            text = buffer.Text;
            source = buffer;
        }

        ImGui.TextUnformatted(buffer.Dirty ? "* " + buffer.Path : buffer.Path);
        var size = new Vector2(
            Math.Max(40, ImGui.GetContentRegionAvail().X),
            Math.Max(80, ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing()));
        if (ImGui.InputTextMultiline("##compare-edit-" + side, ref text, (nuint)Math.Max(text.Length + 1024, 2048), size))
        {
            _session.SetText(side, text);
        }
    }
}
