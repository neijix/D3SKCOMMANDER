using Avalonia.Controls;
using Avalonia.Media;

namespace D3skCommander.App.Controls.FileList;

internal static class FileListLayout
{
    /// <summary>The cursor row of the list that has the focus.</summary>
    public static readonly IBrush CursorBrush = new SolidColorBrush(Color.FromArgb(110, 70, 140, 255));

    /// <summary>The cursor row of the other panel: a faint grey, so only one row reads as "the file I act on".</summary>
    public static readonly IBrush DimCursorBrush = new SolidColorBrush(Color.FromArgb(45, 160, 160, 160));

    public static IBrush CursorBackground(bool isCursor, bool dimmed) =>
        !isCursor ? Brushes.Transparent : dimmed ? DimCursorBrush : CursorBrush;

    public const double RowHeight = 22;

    public const double HeaderHeight = 24;

    /// <summary>The kind icon left of a name: 16 px in the 22 px row, 4 px from the edge.</summary>
    public const double IconSize = 16;

    public const double IconLeft = 4;

    /// <summary>One tile of the thumbnail view and the room the picture gets inside it.</summary>
    public const double TileWidth = 104;

    public const double TileHeight = 96;

    public const double ThumbWidth = 80;

    public const double ThumbHeight = 56;

    /// <summary>Thumbnails are decoded this wide: a little over the picture box, for sharpness.</summary>
    public const double ThumbDecodeWidth = 96;

    /// <summary>The star column fills the rest; the others keep their width.</summary>
    public static ColumnDefinition Definition(FileListColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return column.Star
            ? new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 40 }
            : new ColumnDefinition(column.Width, GridUnitType.Pixel);
    }
}
