using System.Globalization;
using System.Text;
using D3skCommander.Core.Viewer;

namespace D3skCommander.App.Features.Lister;

/// <summary>One row of the viewer list: the gutter (line number or byte offset) and the text.</summary>
public sealed record ViewerLine(string Gutter, string Text);

/// <summary>A mode the switch offers, with its localized label.</summary>
public sealed record ViewerModeChoice(ViewerMode Mode, string Label);

/// <summary>The rows of a text or hex page, and for text the first row of each line (wrapping makes them differ).</summary>
internal sealed record PageRows(IReadOnlyList<ViewerLine> Lines, int[]? FirstRow)
{
    public int RowOfLine(int line) => FirstRow is null ? line : line >= 0 && line < FirstRow.Length ? FirstRow[line] : -1;
}

/// <summary>Builds the viewer's rows on demand: a text page (wrapped or clipped), a hex page, or the table.</summary>
internal static class ViewerPageRows
{
    /// <summary>
    /// The rows of <paramref name="page"/> in <paramref name="mode"/>. With <paramref name="wrap"/>,
    /// a text line is cut so that its gutter and text together fit <paramref name="columns"/> characters.
    /// </summary>
    public static PageRows Build(ViewerPage page, ViewerMode mode, bool wrap, int columns)
    {
        if (mode == ViewerMode.Hex)
        {
            var hex = page.HexRows;
            return new PageRows(new LazyRows(hex.Count, i => new ViewerLine("", hex[i].Format())), null);
        }

        var lines = page.Lines;
        var numbered = page.Start == 0 && page.AtEnd;
        string Gutter(int line) => numbered
            ? (line + 1).ToString(CultureInfo.InvariantCulture).PadLeft(6) + "  "
            : lines[line].Offset.ToString("X8", CultureInfo.InvariantCulture) + "  ";
        var blank = new string(' ', numbered ? 8 : 10);
        if (!wrap)
        {
            return new PageRows(new LazyRows(lines.Count, i => new ViewerLine(Gutter(i), ViewerText.ClipLine(lines[i].Text))), null);
        }

        var spans = new List<LineSpan>(lines.Count);
        var joined = new StringBuilder();
        foreach (var line in lines)
        {
            spans.Add(new LineSpan(joined.Length, joined.Length + line.Text.Length));
            joined.Append(line.Text).Append('\n');
        }

        var text = joined.ToString();
        var visual = ViewerText.WrapRows(text, spans, ViewerText.WrapWidth(columns, blank.Length));
        var first = new int[lines.Count];
        for (var i = visual.Count - 1; i >= 0; i--)
        {
            first[visual[i].Line] = i;
        }

        var rows = new LazyRows(visual.Count, i =>
        {
            var row = visual[i];
            var slice = ViewerText.ClipLine(text.Substring(row.Start, row.End - row.Start));
            return new ViewerLine(row.First ? Gutter(row.Line) : blank, slice);
        });
        return new PageRows(rows, first);
    }

    /// <summary>The table's rows after its filter; none outside table mode.</summary>
    public static IReadOnlyList<ViewerLine> Table(FileViewer viewer)
    {
        if (viewer.Mode != ViewerMode.Table || viewer.Table is not { } table)
        {
            return [];
        }

        var shown = viewer.FilteredRows;
        return new LazyRows(shown.Count, i => new ViewerLine("", ViewerRowText.Row(table, shown[i])));
    }
}
