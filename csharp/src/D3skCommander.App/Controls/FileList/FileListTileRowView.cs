using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace D3skCommander.App.Controls.FileList;

/// <summary>The rows of entries one tile row of the thumbnail view shows: <c>First</c> and how many.</summary>
public sealed record FileListTileSpan(int First, int Count);

/// <summary>One line of tiles. Only the lines in view exist, so only their tiles are built.</summary>
public sealed class FileListTileRowView : StackPanel
{
    public FileListTileRowView()
    {
        Orientation = Orientation.Horizontal;
        Height = FileListLayout.TileHeight;
        Focusable = false;
    }

    public IEnumerable<FileListTileView> Tiles => Children.OfType<FileListTileView>();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Fill();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Clear();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty)
        {
            Fill();
        }
    }

    private void Clear()
    {
        foreach (var tile in Tiles)
        {
            tile.Unbind();
        }

        Children.Clear();
    }

    private void Fill()
    {
        Clear();
        var list = this.FindAncestorOfType<FileListControl>()?.List;
        if (DataContext is not FileListTileSpan span || list is null)
        {
            return;
        }

        for (var i = 0; i < span.Count && span.First + i < list.Items.Count; i++)
        {
            var tile = new FileListTileView();
            tile.Bind(list.Items[span.First + i], span.First + i);
            Children.Add(tile);
        }
    }
}
