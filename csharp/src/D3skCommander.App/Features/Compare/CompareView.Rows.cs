using System.Numerics;
using D3skCommander.Core.Compare;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.Compare;

public sealed partial class CompareView
{
    private void DrawText(CompareOutcome outcome)
    {
        SurfaceText = BlockLabel();
        if (AllEqual(outcome.Rows))
        {
            ImGui.TextUnformatted(_text.Get("compare.identical"));
        }

        DrawRows(outcome);
        DrawnRows = _rows.ToString();
    }

    private void DrawRows(CompareOutcome outcome)
    {
        var rows = outcome.Rows;
        _rows.Clear();
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY;
        var height = Math.Max(ImGui.GetTextLineHeightWithSpacing() * 4, ImGui.GetContentRegionAvail().Y);
        if (!ImGui.BeginTable("compare-rows", 4, flags, new Vector2(0, height)))
        {
            return;
        }

        ImGui.TableSetupColumn("##ln", ImGuiTableColumnFlags.WidthFixed, 48);
        ImGui.TableSetupColumn(_text.Get("compare.left"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##rn", ImGuiTableColumnFlags.WidthFixed, 48);
        ImGui.TableSetupColumn(_text.Get("compare.right"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        DrawClipped(rows);
        ScrollToRow();
        ImGui.EndTable();
    }

    private void DrawClipped(IReadOnlyList<DiffRow> rows)
    {
        var clipper = ImGui.ImGuiListClipper();
        try
        {
            ImGui.Begin(clipper, rows.Count, ImGui.GetTextLineHeightWithSpacing());
            while (ImGui.Step(clipper))
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    DrawDiffRow(rows, i);
                }
            }
        }
        finally
        {
            ImGui.Destroy(clipper);
        }
    }

    private void DrawDiffRow(IReadOnlyList<DiffRow> rows, int index)
    {
        var row = rows[index];
        var tint = SideTint.For(row.Kind);
        var number = InBlock(rows, index) ? SideTint.Mark : tint.Left;
        ImGui.TableNextRow();
        Cell(0, Number(row.LeftNumber), number);
        Cell(1, Shown(row.Left), tint.Left);
        Cell(2, Number(row.RightNumber), tint.Right);
        Cell(3, Shown(row.Right), tint.Right);
        _rows.Append(Shown(row.Left));
        _rows.Append('|');
        _rows.Append(Shown(row.Right));
        _rows.Append('\n');
    }

    private void ScrollToRow()
    {
        if (_session.ScrollTo is not int row)
        {
            return;
        }

        ImGui.SetScrollY(row * ImGui.GetTextLineHeightWithSpacing());
        _session.AcknowledgeScroll();
    }

    private bool InBlock(IReadOnlyList<DiffRow> rows, int index)
    {
        if (_session.Current is not int current)
        {
            return false;
        }

        var span = DiffNavigator.BlockRange(rows, current);
        return index >= span.Start && index < span.End;
    }

    private static void Cell(int column, string text, Vector4 tint)
    {
        ImGui.TableSetColumnIndex(column);
        if (tint.W > 0)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(tint));
        }

        ImGui.TextUnformatted(text);
    }

    private static bool AllEqual(IReadOnlyList<DiffRow> rows)
    {
        if (rows.Count == 0)
        {
            return false;
        }

        foreach (var row in rows)
        {
            if (row.Kind != RowKind.Equal)
            {
                return false;
            }
        }

        return true;
    }

    private static string Number(int? value)
    {
        if (!value.HasValue)
        {
            return "";
        }

        return value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Shown(string? text)
    {
        if (text == null)
        {
            return "";
        }

        return text.Replace("\t", "    ", StringComparison.Ordinal);
    }

    private readonly struct SideTint
    {
        private SideTint(Vector4 left, Vector4 right)
        {
            Left = left;
            Right = right;
        }

        public Vector4 Left { get; }

        public Vector4 Right { get; }

        public static Vector4 Mark { get; } = new(0.35f, 0.55f, 0.95f, 0.45f);

        public static SideTint For(RowKind kind)
        {
            if (kind == RowKind.Changed)
            {
                return new SideTint(new Vector4(1f, 0.8f, 0.2f, 0.35f), new Vector4(1f, 0.8f, 0.2f, 0.35f));
            }

            if (kind == RowKind.LeftOnly)
            {
                return new SideTint(new Vector4(1f, 0.27f, 0.27f, 0.35f), new Vector4(0.5f, 0.5f, 0.5f, 0.18f));
            }

            if (kind == RowKind.RightOnly)
            {
                return new SideTint(new Vector4(0.5f, 0.5f, 0.5f, 0.18f), new Vector4(0.24f, 0.78f, 0.31f, 0.35f));
            }

            return new SideTint(default, default);
        }
    }
}
