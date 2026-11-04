using Avalonia.Controls;

namespace D3skCommander.App.Controls.FileList;

internal static class FileListLayout
{
    public const double RowHeight = 22;

    public static ColumnDefinition Definition(FileListColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        var length = column.Star ? new GridLength(1, GridUnitType.Star) : new GridLength(column.Width);
        return new ColumnDefinition { Width = length };
    }
}
