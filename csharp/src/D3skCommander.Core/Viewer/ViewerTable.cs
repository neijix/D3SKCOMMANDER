namespace D3skCommander.Core.Viewer;

/// <summary>A DBF or catalog table. <see cref="Note"/> is the record count or the catalog root.</summary>
public sealed class ViewerTable
{
    public ViewerTable(
        IReadOnlyList<string> columns,
        IReadOnlyList<int> widths,
        IReadOnlyList<IReadOnlyList<string>> rows,
        string note)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(widths);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(note);
        Columns = columns;
        Widths = widths;
        Rows = rows;
        Note = note;
    }

    public IReadOnlyList<string> Columns { get; }

    /// <summary>Width hint per column, in characters.</summary>
    public IReadOnlyList<int> Widths { get; }

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    public string Note { get; }

    /// <summary>Indexes of rows whose cells contain <paramref name="filter"/>, case-insensitive. Blank keeps every row.</summary>
    public IReadOnlyList<int> Filtered(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var needle = filter.Trim().ToLowerInvariant();
        var hits = new List<int>();
        for (var i = 0; i < Rows.Count; i++)
        {
            if (needle.Length == 0 || RowContains(Rows[i], needle))
            {
                hits.Add(i);
            }
        }

        return hits;
    }

    private static bool RowContains(IReadOnlyList<string> row, string needle)
    {
        for (var i = 0; i < row.Count; i++)
        {
            if (row[i].ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
