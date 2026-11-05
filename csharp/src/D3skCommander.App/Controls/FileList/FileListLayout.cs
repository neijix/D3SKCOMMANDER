using Avalonia.Controls;

namespace D3skCommander.App.Controls.FileList;

internal static class FileListLayout
{
    public const double RowHeight = 22;

    public const double HeaderHeight = 24;

    /// <summary>The star column fills the rest; the others keep their width.</summary>
    public static ColumnDefinition Definition(FileListColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return column.Star
            ? new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 40 }
            : new ColumnDefinition(column.Width, GridUnitType.Pixel);
    }
}
