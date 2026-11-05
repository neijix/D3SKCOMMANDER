namespace D3skCommander.Core.Viewer;

public sealed partial class FileViewer
{
    public int HexRowCount
    {
        get
        {
            if (_bytes.Length == 0)
            {
                return 0;
            }

            return (_bytes.Length + ViewerLimits.HexWidth - 1) / ViewerLimits.HexWidth;
        }
    }

    public bool HexLineIsCurrent(int row)
    {
        if (HexCursor is not int offset)
        {
            return false;
        }

        return offset / ViewerLimits.HexWidth == row;
    }

    public string HexLine(int row)
    {
        var width = ViewerLimits.HexWidth;
        var start = row * width;
        if (row < 0 || start >= _bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        var count = Math.Min(width, _bytes.Length - start);
        return HexRow.Format(start, _bytes.AsSpan(start, count));
    }

    /// <summary>Wrapped rows when <see cref="Wrap"/> is on, otherwise one clipped row per line.</summary>
    public IReadOnlyList<ViewerTextRow> TextRows(int columns)
    {
        Prepare();
        var text = _text;
        if (text is null)
        {
            return [];
        }

        if (!Wrap)
        {
            return PlainRows(text);
        }

        EnsureWrapped(columns);
        if (_wrapped is null)
        {
            return [];
        }

        return WrappedRows(text, _wrapped.Value.Rows);
    }

    private void EnsureWrapped(int columns)
    {
        var text = _text;
        if (text is null)
        {
            return;
        }

        if (_wrapped is not null && _wrapped.Value.Columns == columns)
        {
            return;
        }

        _wrapped = (columns, ViewerText.WrapRows(text, _lines, columns));
    }

    private List<ViewerTextRow> PlainRows(string text)
    {
        var rows = new List<ViewerTextRow>(_lines.Count);
        for (var i = 0; i < _lines.Count; i++)
        {
            var span = _lines[i];
            var slice = text.Substring(span.Start, span.End - span.Start);
            rows.Add(new ViewerTextRow(i, i + 1, ViewerText.ClipLine(slice), TextCursor == i));
        }

        return rows;
    }

    private List<ViewerTextRow> WrappedRows(string text, List<VisualRow> visual)
    {
        var rows = new List<ViewerTextRow>(visual.Count);
        for (var i = 0; i < visual.Count; i++)
        {
            var row = visual[i];
            var slice = text.Substring(row.Start, row.End - row.Start);
            int? number = null;
            if (row.First)
            {
                number = row.Line + 1;
            }

            var current = TextCursor is int line && line == row.Line;
            rows.Add(new ViewerTextRow(row.Line, number, ViewerText.ClipLine(slice), current));
        }

        return rows;
    }
}
