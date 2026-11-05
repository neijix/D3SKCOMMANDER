using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace D3skCommander.App.Controls.FileList;

public sealed class FileListRowView : Grid
{
    private static readonly IBrush MarkedBrush = new SolidColorBrush(Color.FromRgb(200, 30, 20));
    private static readonly IBrush CursorBrush = new SolidColorBrush(Color.FromArgb(110, 70, 140, 255));

    private readonly List<TextBlock> _blocks = [];
    private FileListRow? _row;
    private TextBox? _editor;
    private string? _signature;

    public FileListRowView()
    {
        Height = FileListLayout.RowHeight;
        MinHeight = FileListLayout.RowHeight;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Focusable = false;
    }

    public IReadOnlyList<string> RenderedCells()
    {
        var cells = new string[_blocks.Count];
        for (var i = 0; i < _blocks.Count; i++)
        {
            cells[i] = _blocks[i].Text ?? "";
        }

        return cells;
    }

    internal TextBox? Editor => _editor is { IsVisible: true } ? _editor : null;

    internal void FocusEditor() => ApplyEditor(true);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Apply();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Resubscribe(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty)
        {
            Apply();
        }
    }

    private void Apply()
    {
        Resubscribe(DataContext as FileListRow);
        var list = this.FindAncestorOfType<FileListControl>()?.List;
        if (_row is null || list is null)
        {
            return;
        }

        EnsureGrid(list.CurrentColumns);
        EnsureEditorTemplate();
        Fill();
    }

    private void EnsureEditorTemplate()
    {
        if (_editor is null || _editor.Template is not null)
        {
            return;
        }

        if (!this.TryFindResource("FileListEditorTemplate", out var found) || found is not IControlTemplate template)
        {
            return;
        }

        _editor.Template = template;
    }

    private void Resubscribe(FileListRow? row)
    {
        if (ReferenceEquals(_row, row))
        {
            return;
        }

        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowChanged;
        }

        _row = row;
        if (_row is not null)
        {
            _row.PropertyChanged += OnRowChanged;
        }
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_row is null)
        {
            return;
        }

        if (e.PropertyName == nameof(FileListRow.IsEditing))
        {
            ApplyEditor(true);
            return;
        }

        ApplyFlags();
    }

    private void EnsureGrid(IReadOnlyList<FileListColumn> columns)
    {
        var signature = ColumnSignature(columns);
        if (signature == _signature && _blocks.Count == columns.Count)
        {
            return;
        }

        _signature = signature;
        Children.Clear();
        ColumnDefinitions.Clear();
        _blocks.Clear();
        _editor = null;
        for (var i = 0; i < columns.Count; i++)
        {
            AddCell(columns[i], i);
        }
    }

    private void AddCell(FileListColumn column, int index)
    {
        ColumnDefinitions.Add(FileListLayout.Definition(column));
        var block = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(block, index);
        Children.Add(block);
        _blocks.Add(block);
        if (index == 0)
        {
            AddEditor();
        }
    }

    private void AddEditor()
    {
        _editor = new TextBox
        {
            IsVisible = false,
            MinHeight = 18,
            Margin = new Thickness(2, 0),
            Padding = new Thickness(2, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _editor.PropertyChanged += OnEditorChanged;
        Grid.SetColumn(_editor, 0);
        Children.Add(_editor);
    }

    private void OnEditorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty && _row is not null && _editor is not null)
        {
            _row.EditBuffer = _editor.Text ?? "";
        }
    }

    private void Fill()
    {
        if (_row is null)
        {
            return;
        }

        for (var i = 0; i < _blocks.Count; i++)
        {
            _blocks[i].Text = i < _row.Cells.Count ? _row.Cells[i] : "";
        }

        ApplyFlags();
        ApplyEditor(false);
        ToolTip.SetTip(this, _row.Key.Length == 0 ? null : _row.Key);
    }

    private void ApplyFlags()
    {
        if (_row is null)
        {
            return;
        }

        for (var i = 0; i < _blocks.Count; i++)
        {
            if (_row.IsMarked)
            {
                _blocks[i].Foreground = MarkedBrush;
            }
            else
            {
                _blocks[i].ClearValue(TextBlock.ForegroundProperty);
            }
        }

        Background = _row.IsCursor ? CursorBrush : Brushes.Transparent;
        Opacity = _row.IsHidden ? 0.5 : 1;
        Classes.Set("marked", _row.IsMarked);
        Classes.Set("cursor", _row.IsCursor);
        Classes.Set("parent", _row.IsParent);
        Classes.Set("directory", _row.IsDirectory);
        Classes.Set("link", _row.IsLink);
        Classes.Set("hidden", _row.IsHidden);
        Classes.Set("readonly", _row.IsReadOnly);
    }

    private void ApplyEditor(bool focus)
    {
        if (_editor is null || _row is null)
        {
            return;
        }

        var editing = _row.IsEditing;
        if (_blocks.Count > 0)
        {
            _blocks[0].IsVisible = !editing;
        }

        _editor.IsVisible = editing;
        if (!editing)
        {
            return;
        }

        _editor.Text = _row.EditBuffer;
        if (!focus)
        {
            return;
        }

        var editor = _editor;
        Dispatcher.UIThread.Post(() => FocusWhenShown(editor), DispatcherPriority.Loaded);
    }

    private static void FocusWhenShown(TextBox editor)
    {
        if (!editor.IsVisible)
        {
            return;
        }

        editor.Focus();
        editor.SelectAll();
    }

    private static string ColumnSignature(IReadOnlyList<FileListColumn> columns)
    {
        var text = new StringBuilder();
        foreach (var column in columns)
        {
            text.Append(column.HeaderKey);
            text.Append(':');
            text.Append(column.Width.ToString(CultureInfo.InvariantCulture));
            text.Append(column.Star ? "*" : "");
            text.Append('|');
        }

        return text.ToString();
    }
}
