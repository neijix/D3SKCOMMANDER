using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace D3skCommander.App.Controls.FileList;

/// <summary>The thumbnail view: the same rows laid out as lines of tiles, virtualized by line.</summary>
public sealed partial class FileListControl
{
    private const double ScrollBarRoom = 18;

    private int _columnsShown = -1;

    /// <summary>Tile lines that exist as controls now (the viewport and a small buffer).</summary>
    public int RealizedTileLines => Tiles.GetRealizedContainers().Count();

    /// <summary>Tiles that exist as controls now.</summary>
    public int RealizedTileCount => RealizedTiles().Count();

    public int TilesPerLine => _list.ThumbColumns;

    public bool IsTileRealized(int index) => TileAt(index) is not null;

    /// <summary>The icon key of a realized tile (<c>folder</c>, <c>image</c>, ...), or null.</summary>
    public string? TileIconKey(int index) => TileAt(index)?.IconKey;

    /// <summary>Whether a realized tile shows a decoded picture instead of its icon.</summary>
    public bool TileShowsThumbnail(int index) => TileAt(index)?.ShowsThumbnail ?? false;

    public string? TileName(int index) => TileAt(index)?.NameText;

    /// <summary>The icon key of a realized Full or Brief row, or null when the row has no icon.</summary>
    public string? IconKey(int index) => RowViewAt(index)?.IconKey;

    private IEnumerable<FileListTileView> RealizedTiles()
    {
        foreach (var container in Tiles.GetRealizedContainers())
        {
            foreach (var view in container.GetVisualDescendants().OfType<FileListTileRowView>())
            {
                foreach (var tile in view.Tiles)
                {
                    yield return tile;
                }
            }
        }
    }

    private FileListTileView? TileAt(int index)
    {
        foreach (var tile in RealizedTiles())
        {
            if (tile.Index == index)
            {
                return tile;
            }
        }

        return null;
    }

    private void StartThumbs()
    {
        Tiles.ItemTemplate = new FuncDataTemplate<FileListTileSpan>((_, _) => new FileListTileRowView(), true);
        Tiles.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        Tiles.SizeChanged += (_, _) => OnTilesResized();
    }

    private void ShowTiles()
    {
        Rows.IsVisible = false;
        Rows.ItemsSource = null;
        Header.IsVisible = false;
        Tiles.IsVisible = true;
        UseColumns(ColumnsFor(Tiles.Bounds.Width));
        _columnsShown = _list.ThumbColumns;
        Tiles.ItemsSource = Spans(_list.Items.Count, _list.ThumbColumns);
    }

    private void OnTilesResized()
    {
        if (!_list.ThumbsActive)
        {
            return;
        }

        var columns = ColumnsFor(Tiles.Bounds.Width);
        if (columns != _columnsShown && Tiles.Bounds.Width > 0)
        {
            ShowTiles();
        }
    }

    private void UseColumns(int columns) => _list.ThumbColumns = columns;

    private int ColumnsFor(double width)
    {
        if (width <= 0)
        {
            return _list.ThumbColumns;
        }

        return Math.Max(1, (int)Math.Floor((width - ScrollBarRoom) / FileListLayout.TileWidth));
    }

    private static List<FileListTileSpan> Spans(int total, int columns)
    {
        var spans = new List<FileListTileSpan>();
        for (var first = 0; first < total; first += columns)
        {
            spans.Add(new FileListTileSpan(first, Math.Min(columns, total - first)));
        }

        return spans;
    }
}
