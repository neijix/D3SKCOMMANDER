using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using D3skCommander.App.Icons;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Controls.FileList;

/// <summary>One tile of the thumbnail view: the picture (or the kind icon) with the name under it.</summary>
public sealed class FileListTileView : Border
{
    private static readonly IBrush MarkedBrush = new SolidColorBrush(Color.FromRgb(200, 30, 20));

    private readonly FileKindIconControl _icon = new(40);
    private readonly Image _picture = new() { Stretch = Stretch.Uniform, IsVisible = false };
    private readonly TextBlock _name = new();
    private FileListRow? _row;
    private string? _wanted;

    public FileListTileView()
    {
        Width = FileListLayout.TileWidth;
        Height = FileListLayout.TileHeight;
        CornerRadius = new CornerRadius(3);
        Focusable = false;
        _name.TextAlignment = TextAlignment.Center;
        _name.TextWrapping = TextWrapping.Wrap;
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        _name.MaxLines = 2;
        _name.FontSize = 12;
        _name.Margin = new Thickness(3, 0);
        _name.VerticalAlignment = VerticalAlignment.Top;
        var thumb = new Grid
        {
            Width = FileListLayout.ThumbWidth,
            Height = FileListLayout.ThumbHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 2),
        };
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _icon.VerticalAlignment = VerticalAlignment.Center;
        thumb.Children.Add(_icon);
        thumb.Children.Add(_picture);
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(thumb);
        stack.Children.Add(_name);
        Child = stack;
    }

    /// <summary>The row index this tile shows, or -1 when unbound.</summary>
    public int Index { get; private set; } = -1;

    public string IconKey => _icon.IconKey;

    public bool ShowsThumbnail => _picture.IsVisible;

    public string NameText => _name.Text ?? "";

    public void Bind(FileListRow row, int index)
    {
        Unbind();
        _row = row;
        Index = index;
        _row.PropertyChanged += OnRowChanged;
        _icon.Kind = row.Kind;
        _name.Text = row.Cells.Count > 0 ? row.Cells[0] : "";
        ToolTip.SetTip(this, row.Key.Length == 0 ? null : row.Key);
        ShowPicture(null);
        ApplyFlags();
        RequestThumbnail(row);
    }

    public void Unbind()
    {
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowChanged;
        }

        _row = null;
        _wanted = null;
        Index = -1;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => ApplyFlags();

    /// <summary>Redraws the cursor and marks (the list's dimmed state changed).</summary>
    internal void RefreshFlags() => ApplyFlags();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyFlags();
    }

    private void ApplyFlags()
    {
        if (_row is null)
        {
            return;
        }

        Background = FileListLayout.CursorBackground(_row.IsCursor, this.FindAncestorOfType<FileListControl>()?.IsCursorDimmed == true);
        Opacity = _row.IsHidden ? 0.5 : 1;
        if (_row.IsMarked)
        {
            _name.Foreground = MarkedBrush;
        }
        else
        {
            _name.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    private void RequestThumbnail(FileListRow row)
    {
        var entry = row.Source as Entry;
        if (entry is null || entry.IsDirectory || entry.IsLink || !ThumbnailCache.CanThumbnail(entry.Path))
        {
            return;
        }

        var key = ThumbnailCache.KeyOf(entry.Path, entry.Modified, (int)FileListLayout.ThumbDecodeWidth);
        _wanted = key;
        ThumbnailCache.Shared.Request(entry.Path, entry.Modified, (int)FileListLayout.ThumbDecodeWidth, bitmap =>
        {
            if (_wanted == key)
            {
                ShowPicture(bitmap);
            }
        });
    }

    private void ShowPicture(Bitmap? bitmap)
    {
        _picture.Source = bitmap;
        _picture.IsVisible = bitmap is not null;
        _icon.IsVisible = bitmap is null;
    }
}
