namespace D3skCommander.Core.Panels;

/// <summary>One column of a set: which, and its width in DIPs (0 = auto).</summary>
public sealed class ColumnSetColumn
{
    private double _width;

    public ColumnSetColumn()
    {
    }

    public ColumnSetColumn(ColumnId id, double width = 0)
    {
        Id = id;
        Width = width;
    }

    public ColumnId Id { get; set; } = ColumnId.Size;

    public double Width
    {
        get => _width;
        set => _width = double.IsFinite(value) ? Math.Clamp(value, 0, ColumnSet.MaxWidth) : 0;
    }
}

/// <summary>A named, ordered list of columns after the name, which is always first.</summary>
public sealed class ColumnSet
{
    public const double MinWidth = 30;

    public const double MaxWidth = 1000;

    /// <summary>The name column keeps this much before any other column shows.</summary>
    public const float NameMinimum = 160f;

    private string _name = "";
    private List<ColumnSetColumn> _columns = [];

    public ColumnSet()
    {
    }

    public ColumnSet(string name, IEnumerable<ColumnSetColumn> columns)
    {
        Name = name;
        Columns = [.. columns];
    }

    public string Name
    {
        get => _name;
        set => _name = value ?? "";
    }

    public List<ColumnSetColumn> Columns
    {
        get => _columns;
        set => _columns = value ?? [];
    }

    public bool Has(ColumnId id) => _columns.Any(column => column.Id == id);

    public ColumnSet Clone() => new(_name, _columns.Select(column => new ColumnSetColumn(column.Id, column.Width)));

    /// <summary>The same set with each column once, the name column removed and widths in range.</summary>
    public ColumnSet Normalized()
    {
        var seen = new HashSet<ColumnId>();
        var kept = new List<ColumnSetColumn>();
        foreach (var column in _columns)
        {
            if (column.Id == ColumnId.Name || !Enum.IsDefined(column.Id) || !seen.Add(column.Id))
            {
                continue;
            }

            var width = column.Width <= 0 ? 0 : Math.Max(ColumnInfo.MinWidth(column.Id), column.Width);
            kept.Add(new ColumnSetColumn(column.Id, width));
        }

        return new ColumnSet(_name, kept);
    }

    /// <summary>
    /// The columns that fit in <paramref name="width"/>, counting the name column's minimum and
    /// <paramref name="gap"/> after each extra column. A narrow pane drops the least useful first
    /// (<see cref="ColumnInfo.DropOrder"/>): group, owner, accessed, created, kind, attributes, extension, date, size.
    /// </summary>
    public ColumnSet Fitting(float width, float gap)
    {
        var left = new List<ColumnSetColumn>(_columns);
        foreach (var drop in ColumnInfo.DropOrder)
        {
            if (Used(left, gap) <= width)
            {
                break;
            }

            left.RemoveAll(column => column.Id == drop);
        }

        return new ColumnSet(_name, left);
    }

    /// <summary>Equal when the order, columns and widths are equal (the name is not compared).</summary>
    public string Signature() =>
        string.Join('|', _columns.Select(column => $"{(int)column.Id}:{column.Width.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}"));

    private static float Used(List<ColumnSetColumn> columns, float gap)
    {
        var total = NameMinimum;
        foreach (var column in columns)
        {
            var own = column.Width > 0 ? (float)Math.Max(column.Width, ColumnInfo.MinWidth(column.Id)) : ColumnInfo.FitWidth(column.Id);
            total += own + gap;
        }

        return total;
    }
}
