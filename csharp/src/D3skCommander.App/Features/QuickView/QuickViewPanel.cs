using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaEdit;
using D3skCommander.App.Features.Editor;
using D3skCommander.App.Features.Lister;
using D3skCommander.Core.Viewer;

namespace D3skCommander.App.Features.QuickView;

/// <summary>
/// What the quick view and the preview mode put over a pane: a text editor with line numbers and colours, a
/// picture, a video, the pages of a PDF, the list of an archive, the lister's own view for any other file, a short
/// summary for a folder, nothing for the rest. It owns the bitmaps it shows and frees them when they go.
/// </summary>
internal sealed class QuickViewPanel : ContentControl
{
    // The list's row padding (8 px) and its scroll bar: not text width.
    private const double Chrome = 32;
    private readonly Control _viewer;
    private readonly ViewerViewModel _model;
    private readonly List<Bitmap> _bitmaps = [];
    private readonly TextBlock _summary = new()
    {
        Margin = new Avalonia.Thickness(8),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private TextEditor? _editor;
    private EditorView? _edit;
    private StackPanel? _pages;

    public QuickViewPanel(ViewerViewModel viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        _model = viewer;
        _viewer = viewer.CreateContent();
        Styles.Add(PreviewViews.EditorStyles());
        SizeChanged += (_, e) => FitColumns(e.NewSize.Width);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public bool ShowsViewer => ReferenceEquals(Content, _viewer);

    public string SummaryText => _summary.Text ?? "";

    /// <summary>The text in the editor (read-only or editable) while it is shown; null otherwise.</summary>
    public string? ShownText => Content switch
    {
        EditorView { DataContext: EditorViewModel model } => model.Buffer.Text,
        TextEditor editor when ReferenceEquals(editor, _editor) => editor.Text,
        _ => null,
    };

    /// <summary>The colouring the editor uses, by name ("C#", "XML"…); null for plain text or no editor.</summary>
    public string? ShownHighlighting => Content switch
    {
        EditorView view => view.Surface.SyntaxHighlighting?.Name,
        TextEditor editor when ReferenceEquals(editor, _editor) => editor.SyntaxHighlighting?.Name,
        _ => null,
    };

    /// <summary>The editable text shown; null when the preview shows something else.</summary>
    public EditorView? ShownEditor => Content as EditorView;

    /// <summary>The picture shown; null when the content is not a picture.</summary>
    public Bitmap? ShownPicture => (Content as Image)?.Source as Bitmap;

    /// <summary>How many PDF pages are on screen so far.</summary>
    public int PageCount => _pages is not null && Content is ScrollViewer { Content: StackPanel shown } && ReferenceEquals(shown, _pages)
        ? _pages.Children.Count
        : 0;

    /// <summary>The rows of the archive shown; empty when no archive is.</summary>
    public IReadOnlyList<ArchivePreviewRow> ArchiveRows { get; private set; } = [];

    public void ShowViewer() => Replace(_viewer);

    public void ShowSummary(string text)
    {
        _summary.Text = text;
        Replace(_summary);
    }

    public void ShowNothing() => Replace(null);

    public void ShowText(string text, string path)
    {
        _editor ??= PreviewViews.TextEditor();
        PreviewViews.ShowText(_editor, text, path);
        Replace(_editor);
    }

    /// <summary>Shows a text file the user may change, with the editor's menu bar.</summary>
    public void ShowEditor(EditorViewModel model)
    {
        _edit ??= new EditorView();
        _edit.DataContext = model;
        Replace(_edit);
    }

    /// <summary>Shows <paramref name="picture"/>, which the panel now owns.</summary>
    public void ShowPicture(Bitmap picture)
    {
        var image = PreviewViews.Picture();
        image.Source = picture;
        Replace(image);
        _bitmaps.Add(picture);
    }

    /// <summary>The video being played; null when the content is not a video.</summary>
    public VideoPreview? ShownVideo => Content as VideoPreview;

    /// <summary>Plays <paramref name="video"/>, which the panel now owns: it stops when the content changes.</summary>
    public void ShowVideo(VideoPreview video) => Replace(video);

    /// <summary>An empty page list; <see cref="AddPage"/> fills it while it is still shown.</summary>
    public void StartPages()
    {
        var (view, pages) = PreviewViews.PdfPages();
        Replace(view);
        _pages = pages;
    }

    /// <summary>Adds <paramref name="page"/> (owned by the panel from now on) under the others.</summary>
    public void AddPage(Bitmap page)
    {
        if (_pages is null)
        {
            page.Dispose();
            return;
        }

        PreviewViews.AddPage(_pages, page);
        _bitmaps.Add(page);
    }

    public void ShowArchive(string header, IReadOnlyList<ArchivePreviewRow> rows)
    {
        Replace(PreviewViews.Archive(header, rows));
        ArchiveRows = rows;
    }

    /// <summary>Frees what the panel holds; called when the preview goes away.</summary>
    public void Release() => Replace(null);

    // Swaps the content, then frees the bitmaps the old content showed (after it let go of them).
    private void Replace(Control? content)
    {
        var old = _bitmaps.ToArray();
        _bitmaps.Clear();
        _pages = null;
        ArchiveRows = [];
        if (_editor is not null && !ReferenceEquals(content, _editor))
        {
            _editor.Document = new AvaloniaEdit.Document.TextDocument();
        }

        var video = Content as VideoPreview;
        Content = content;
        if (!ReferenceEquals(video, content))
        {
            video?.Dispose();
        }

        foreach (var bitmap in old)
        {
            bitmap.Dispose();
        }
    }

    /// <summary>
    /// The viewer wraps at the width of the pane it sits in: the view measures it at its first
    /// layout, before the pane has its size, so the panel tells the model again when it resizes.
    /// </summary>
    private void FitColumns(double width)
    {
        if (_viewer is ViewerView view && width > Chrome)
        {
            _model.SetColumns(ViewerText.ColumnsIn(width - Chrome, view.CharWidth()));
        }
    }
}
