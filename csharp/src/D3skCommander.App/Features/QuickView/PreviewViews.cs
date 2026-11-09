using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using D3skCommander.Core.Archives;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Features.QuickView;

/// <summary>One line of an archive's list in the preview.</summary>
public sealed record ArchivePreviewRow(string Name, string Size, string Modified);

/// <summary>
/// The controls the preview panel shows besides the lister: a read-only text editor with line numbers and
/// colours by extension, a picture fitted to the pane, the pages of a PDF and the list of an archive.
/// </summary>
internal static class PreviewViews
{
    private static readonly Uri EditorTheme = new("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml");

    public static IStyle EditorStyles() => new Avalonia.Markup.Xaml.Styling.StyleInclude(EditorTheme) { Source = EditorTheme };

    public static TextEditor TextEditor()
    {
        var editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = true,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(4, 2),
        };
        if (Application.Current?.TryFindResource("MonoFont", out var font) == true && font is FontFamily family)
        {
            editor.FontFamily = family;
        }

        return editor;
    }

    /// <summary>Puts <paramref name="text"/> in the editor, coloured as the extension of <paramref name="path"/> says.</summary>
    public static void ShowText(TextEditor editor, string text, string path)
    {
        editor.SyntaxHighlighting = HighlightingFor(path);
        editor.Document = new AvaloniaEdit.Document.TextDocument(text);
        editor.ScrollToHome();
    }

    /// <summary>The colouring for a file name; null (plain text) when its extension has none.</summary>
    public static IHighlightingDefinition? HighlightingFor(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var alias = extension switch
        {
            ".csproj" or ".props" or ".targets" or ".xaml" or ".axaml" or ".config" or ".svg" or ".resx" or ".nuspec" or ".plist" => ".xml",
            ".htm" or ".xhtml" => ".html",
            ".ts" or ".tsx" or ".jsx" or ".mjs" or ".cjs" => ".js",
            ".h" or ".hpp" or ".cc" or ".cxx" or ".c" => ".cpp",
            ".psm1" or ".psd1" => ".ps1",
            ".markdown" => ".md",
            _ => extension,
        };
        return alias.Length == 0 ? null : HighlightingManager.Instance.GetDefinitionByExtension(alias);
    }

    public static Image Picture() => new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(4) };

    /// <summary>The pages of a PDF, one under the other; <see cref="AddPage"/> fills it.</summary>
    public static (ScrollViewer View, StackPanel Pages) PdfPages()
    {
        var pages = new StackPanel { Spacing = 8, Margin = new Thickness(6) };
        return (new ScrollViewer { Content = pages, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, pages);
    }

    public static void AddPage(StackPanel pages, Bitmap page) =>
        pages.Children.Add(new Border
        {
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 6, Color = Color.FromArgb(0x50, 0, 0, 0) }),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Image { Source = page, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
        });

    /// <summary>A header line over the rows of an archive.</summary>
    public static Control Archive(string header, IReadOnlyList<ArchivePreviewRow> rows)
    {
        var list = new ListBox { ItemsSource = rows, ItemTemplate = RowTemplate() };
        var title = new TextBlock { Text = header, Margin = new Thickness(8, 6), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var panel = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);
        panel.Children.Add(list);
        return panel;
    }

    /// <summary>The rows of an archive's list: folders first, then files, each by name.</summary>
    public static IReadOnlyList<ArchivePreviewRow> RowsOf(IReadOnlyList<ArchiveEntry> entries) =>
        entries
            .OrderBy(entry => entry.IsDirectory ? 0 : 1)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ArchivePreviewRow(
                entry.IsDirectory ? entry.Path.TrimEnd('/') + "/" : entry.Path,
                entry.IsDirectory ? "" : LocalFileSystem.FormatSize((ulong)Math.Max(0, entry.Size)),
                LocalFileSystem.FormatTime(entry.Modified)))
            .ToList();

    private static FuncDataTemplate<ArchivePreviewRow> RowTemplate() => new((_, _) =>
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        grid.Children.Add(Cell(nameof(ArchivePreviewRow.Name), 0, TextAlignment.Left));
        grid.Children.Add(Cell(nameof(ArchivePreviewRow.Size), 1, TextAlignment.Right));
        grid.Children.Add(Cell(nameof(ArchivePreviewRow.Modified), 2, TextAlignment.Right));
        return grid;
    });

    private static TextBlock Cell(string property, int column, TextAlignment alignment)
    {
        var cell = new TextBlock
        {
            [!TextBlock.TextProperty] = new Binding(property),
            TextAlignment = alignment,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(column == 0 ? 0 : 12, 0, 0, 0),
            MinWidth = column == 1 ? 70 : 0,
        };
        Grid.SetColumn(cell, column);
        return cell;
    }
}
