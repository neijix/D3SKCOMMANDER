namespace D3skCommander.Core.Panels;

/// <summary>A column of the full view. <see cref="Name"/> is always first and not part of a <see cref="ColumnSet"/>.</summary>
public enum ColumnId
{
    Name,
    Extension,
    Size,
    Modified,
    Created,
    Accessed,
    Attributes,
    Owner,
    Group,
    Kind,
}

/// <summary>What each column is called in the localizer and how wide it is by default.</summary>
public static class ColumnInfo
{
    /// <summary>Columns a set can hold, in the order the editor lists them.</summary>
    public static IReadOnlyList<ColumnId> Optional { get; } =
    [
        ColumnId.Extension,
        ColumnId.Size,
        ColumnId.Modified,
        ColumnId.Created,
        ColumnId.Accessed,
        ColumnId.Attributes,
        ColumnId.Owner,
        ColumnId.Group,
        ColumnId.Kind,
    ];

    /// <summary>Localizer key of the header. The first four keep v0.1's keys.</summary>
    public static string HeaderKey(ColumnId id) => id switch
    {
        ColumnId.Name => "name",
        ColumnId.Extension => "ext",
        ColumnId.Size => "size",
        ColumnId.Modified => "date",
        ColumnId.Attributes => "attr",
        ColumnId.Created => "col.created",
        ColumnId.Accessed => "col.accessed",
        ColumnId.Owner => "col.owner",
        ColumnId.Group => "col.group",
        ColumnId.Kind => "col.kind",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown column."),
    };

    /// <summary>The column behind a header key, or null for a key that is not one of the columns.</summary>
    public static ColumnId? FromHeaderKey(string key)
    {
        foreach (var id in Enum.GetValues<ColumnId>())
        {
            if (string.Equals(HeaderKey(id), key, StringComparison.Ordinal))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>Width in DIPs of a column set to auto (0).</summary>
    public static double AutoWidth(ColumnId id) => id switch
    {
        ColumnId.Extension => 72,
        ColumnId.Size => 96,
        ColumnId.Modified or ColumnId.Created or ColumnId.Accessed => 148,
        ColumnId.Attributes or ColumnId.Owner or ColumnId.Group => 96,
        ColumnId.Kind => 120,
        _ => 0,
    };

    /// <summary>Width counted when deciding which columns fit a narrow pane (v0.1's points for the first four).</summary>
    public static float FitWidth(ColumnId id) => id switch
    {
        ColumnId.Extension => Columns.ExtensionWidth,
        ColumnId.Size => Columns.SizeWidth,
        ColumnId.Modified => 148f,
        ColumnId.Attributes => Columns.AttributesWidth,
        ColumnId.Created or ColumnId.Accessed => 148f,
        ColumnId.Owner or ColumnId.Group => 70f,
        ColumnId.Kind => 100f,
        _ => 0f,
    };

    /// <summary>The narrowest a column may be shown: a date column always holds a full "yyyy-MM-dd HH:mm".</summary>
    public static double MinWidth(ColumnId id) =>
        id is ColumnId.Modified or ColumnId.Created or ColumnId.Accessed ? 148 : ColumnSet.MinWidth;

    /// <summary>Whether sorting by the column is supported: the others would read the disk for every comparison.</summary>
    public static bool IsSortable(ColumnId id) => id is ColumnId.Name or ColumnId.Extension or ColumnId.Size
        or ColumnId.Modified or ColumnId.Attributes or ColumnId.Kind;

    /// <summary>Columns a narrow pane drops first, in order.</summary>
    internal static IReadOnlyList<ColumnId> DropOrder { get; } =
    [
        ColumnId.Group,
        ColumnId.Owner,
        ColumnId.Accessed,
        ColumnId.Created,
        ColumnId.Kind,
        ColumnId.Attributes,
        ColumnId.Extension,
        ColumnId.Modified,
        ColumnId.Size,
    ];
}
