using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using Xunit;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Controls.FileLists;

public class FileListTests
{
    [AvaloniaFact]
    public void OneVirtualizedListServesFullAndBrief()
    {
        var entries = FileListSamples.Files(400);
        FileListHarness.Use(entries, (window, list) =>
        {
            var full = list.FindControl<ListBox>("Rows");
            Assert.NotNull(full);
            AssertUnrealized(full, entries.Count);
            Assert.Equal("f0", list.CellText(0, list.ColumnIndex("name")));
            list.View = FileListView.Brief;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(full, list.FindControl<ListBox>("Rows"));
            AssertUnrealized(full, entries.Count);
            Assert.Equal("f0.txt", list.CellText(0, 0));
            Assert.Null(list.HeaderText("ext"));
            Assert.Equal(-1, list.ColumnIndex("size"));
        }, height: 220);
    }

    [AvaloniaFact]
    public void FullViewShowsTheEntryFields()
    {
        var text = TextCatalog.Load(Language.En, Language.En);
        var entries = SampleEntries();
        FileListHarness.Use(entries, (_, list) =>
        {
            AssertHeaders(list, text);
            AssertCells(list, text);
            AssertFlags(list);
            Assert.Equal(list.CellText(1, 0), list.RenderedCells(1)[0]);
            Assert.Equal("txt", list.RenderedCells(1)[1]);
            Assert.Contains(LocalFileSystem.FormatGrouped(1_234_567), list.RenderedCells(1));
        }, localizer: text);
    }

    [AvaloniaFact]
    public void HeadersFollowTheLanguage()
    {
        var text = TextCatalog.Load(Language.Fr, Language.Fr);
        var entries = new[] { FileListSamples.Folder("docs", "/t/docs") };
        FileListHarness.Use(entries, (_, list) =>
        {
            Assert.Equal(text.Get("name"), list.HeaderText("name"));
            Assert.Equal("Nom", list.HeaderText("name"));
            Assert.Equal("Taille", list.HeaderText("size"));
            Assert.NotEqual(text.Get("name", Language.En), list.HeaderText("name"));
            Assert.Equal(text.Get("filelist.directory"), list.CellText(0, list.ColumnIndex("size")));
            Assert.Equal("<Dossier>", list.CellText(0, list.ColumnIndex("size")));
        }, localizer: text);
    }

    [AvaloniaFact]
    public void APressOnAFileNameSelectsItEvenIfTheMouseMovesOrLingers()
    {
        AssertPressSurvives(0, 1);
        AssertPressSurvives(12, 1);
        AssertPressSurvives(0, 70);
    }

    [AvaloniaFact]
    public void ACtrlPressTogglesAndAPlainPressClearsTheSelection()
    {
        var entries = new[]
        {
            FileListSamples.File("five.txt", "/t/five.txt", 5, "rw-r--r--"),
            FileListSamples.File("six.txt", "/t/six.txt", 6, "rw-r--r--"),
        };
        FileListHarness.Use(entries, (window, list) =>
        {
            var ctrl = RawInputModifiers.Control;
            FileListHarness.Press(window, FileListHarness.RowCenter(list, window, 0), ctrl, 0, 1);
            FileListHarness.Press(window, FileListHarness.RowCenter(list, window, 1), ctrl, 0, 1);
            Assert.Equal(2, list.MarkedCount);
            Assert.True(list.IsMarked("/t/five.txt"));
            Assert.True(list.IsMarked("/t/six.txt"));
            FileListHarness.Press(window, FileListHarness.RowCenter(list, window, 0), ctrl, 0, 1);
            Assert.Equal(1, list.MarkedCount);
            Assert.False(list.IsMarked("/t/five.txt"));
            Assert.True(list.IsMarked("/t/six.txt"));
            FileListHarness.Press(window, FileListHarness.RowCenter(list, window, 0), RawInputModifiers.None, 0, 1);
            Assert.Equal(0, list.MarkedCount);
            Assert.Equal(0, list.CursorIndex);
            FileListHarness.Press(window, FileListHarness.RowCenter(list, window, 1), RawInputModifiers.None, 12, 70);
            Assert.Equal(0, list.MarkedCount);
            Assert.Equal(1, list.CursorIndex);
        });
    }

    [AvaloniaFact]
    public void AShiftPressAddsTheRangeAndCtrlSkipsTheParent()
    {
        var entries = new List<Entry> { FileListSamples.Parent("/t") };
        entries.AddRange(FileListSamples.Files(4));
        FileListHarness.Use(entries, (window, list) =>
        {
            FileListHarness.Press(window, FileListHarness.RowCenter(list, window, 0), RawInputModifiers.Control, 0, 1);
            Assert.Equal(0, list.MarkedCount);
            Assert.Equal(0, list.CursorIndex);
            list.CursorIndex = 1;
            FileListHarness.Press(window, FileListHarness.RowCenter(list, window, 3), RawInputModifiers.Shift, 0, 1);
            Assert.Equal(3, list.MarkedCount);
            Assert.True(list.IsMarked("/t/f0.txt"));
            Assert.True(list.IsMarked("/t/f2.txt"));
            Assert.False(list.IsMarked("/t"));
            Assert.Equal(3, list.CursorIndex);
        });
    }

    [AvaloniaFact]
    public void InsertAndSpaceMarkTheCursorEntry()
    {
        var entries = new[]
        {
            FileListSamples.File("a.txt", "/t/a.txt", 1, "rw-r--r--"),
            FileListSamples.Folder("docs", "/t/docs"),
            FileListSamples.File("b.txt", "/t/b.txt", 2, "rw-r--r--"),
        };
        FileListHarness.Use(entries, (window, list) =>
        {
            var folders = 0;
            list.DirectoryMarked += (_, _) => folders++;
            Assert.Equal(0, list.CursorIndex);
            FileListHarness.Key(window, Key.Insert);
            Assert.True(list.IsMarked("/t/a.txt"));
            Assert.Equal(1, list.CursorIndex);
            FileListHarness.Key(window, Key.Space);
            Assert.True(list.IsMarked("/t/docs"));
            Assert.Equal(1, list.CursorIndex);
            Assert.Equal(1, folders);
            FileListHarness.Key(window, Key.Space);
            Assert.False(list.IsMarked("/t/docs"));
            Assert.Equal(1, folders);
            list.CursorIndex = 2;
            FileListHarness.Key(window, Key.Space);
            Assert.True(list.IsMarked("/t/b.txt"));
            Assert.Equal(2, list.CursorIndex);
            FileListHarness.Key(window, Key.Space);
            Assert.False(list.IsMarked("/t/b.txt"));
        });
    }

    [AvaloniaFact]
    public void PageUpAndPageDownMoveByTwentyRows()
    {
        FileListHarness.Use(FileListSamples.Files(50), (window, list) =>
        {
            Assert.Equal(0, list.CursorIndex);
            FileListHarness.Key(window, Key.PageDown);
            Assert.Equal(20, list.CursorIndex);
            FileListHarness.Key(window, Key.PageDown);
            Assert.Equal(40, list.CursorIndex);
            FileListHarness.Key(window, Key.PageDown);
            Assert.Equal(49, list.CursorIndex);
            FileListHarness.Key(window, Key.PageUp);
            Assert.Equal(29, list.CursorIndex);
            FileListHarness.Key(window, Key.PageUp);
            Assert.Equal(9, list.CursorIndex);
            FileListHarness.Key(window, Key.PageUp);
            Assert.Equal(0, list.CursorIndex);
            FileListHarness.Key(window, Key.End);
            Assert.Equal(49, list.CursorIndex);
            FileListHarness.Key(window, Key.Home);
            Assert.Equal(0, list.CursorIndex);
            FileListHarness.Key(window, Key.Down);
            Assert.Equal(1, list.CursorIndex);
            FileListHarness.Key(window, Key.Up);
            Assert.Equal(0, list.CursorIndex);
        });
    }

    [AvaloniaFact]
    public void RenameEditsTheNameInPlace()
    {
        var entries = new[]
        {
            FileListSamples.Parent("/t"),
            FileListSamples.File("report.txt", "/t/report.txt", 4, "rw-r--r--"),
        };
        FileListHarness.Use(entries, (window, list) =>
        {
            list.CursorIndex = 0;
            Assert.False(list.BeginRename());
            list.CursorIndex = 1;
            string? committed = null;
            list.RenameCommitted += (_, e) => committed = e.Name;
            Assert.True(list.BeginRename());
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var editor = VisibleEditor(list);
            Assert.Equal("report.txt", editor.Text);
            Assert.Equal(list.FindControl<ListBox>("Rows")!.ContainerFromIndex(1), editor.FindAncestorOfType<ListBoxItem>());
            editor.Text = "";
            window.KeyTextInput("new.txt");
            Assert.Equal("new.txt", editor.Text);
            FileListHarness.Key(window, Key.Enter);
            Assert.Equal("new.txt", committed);
            Assert.False(list.IsRenaming);
        });
    }

    [AvaloniaFact]
    public void EscapeCancelsRenameAndAnInvalidNameStaysOpen()
    {
        var entries = new[] { FileListSamples.File("report.txt", "/t/report.txt", 4, "rw-r--r--") };
        FileListHarness.Use(entries, (window, list) =>
        {
            var commits = 0;
            list.RenameCommitted += (_, _) => commits++;
            Assert.True(list.BeginRename());
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var editor = VisibleEditor(list);
            editor.Text = "../nope";
            FileListHarness.Key(window, Key.Enter);
            Assert.True(list.IsRenaming);
            Assert.Equal(0, commits);
            FileListHarness.Key(window, Key.Escape);
            Assert.False(list.IsRenaming);
            Assert.Equal(0, commits);
            Assert.Equal("report", list.CellText(0, list.ColumnIndex("name")));
        });
    }

    [AvaloniaFact]
    public void CustomRowsUseTheSameList()
    {
        var text = TextCatalog.Load(Language.En, Language.En);
        var columns = new FileListColumn[] { new("name", 1, true), new("path", 180) };
        var rows = new[] { new FileListRow("k", ["alpha", "/a"], "alpha", true, false, false, null) };
        FileListHarness.Use(FileListSamples.Files(1), (_, list) =>
        {
            var box = list.FindControl<ListBox>("Rows");
            list.ShowRows(columns, rows);
            Assert.Same(box, list.FindControl<ListBox>("Rows"));
            Assert.Equal(text.Get("name"), list.HeaderText("name"));
            Assert.Equal(text.Get("path"), list.HeaderText("path"));
            Assert.Equal("alpha", list.CellText(0, 0));
            Assert.Equal("/a", list.CellText(0, 1));
        }, localizer: text);
    }

    private static void AssertPressSurvives(double shift, int frames)
    {
        var entries = new[]
        {
            FileListSamples.File("file number 5 with a rather long name.txt", "/t/n5.txt", 5, "rw-r--r--"),
        };
        FileListHarness.Use(entries, (window, list) =>
        {
            var at = FileListHarness.RowCenter(list, window, 0);
            FileListHarness.Press(window, at, RawInputModifiers.Control, shift, frames);
            Assert.True(list.IsMarked("/t/n5.txt"), "move " + shift.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(1, list.MarkedCount);
            Assert.Equal(0, list.CursorIndex);
            Assert.True(list.RowHasClass(0, "marked"));
        });
    }

    private static void AssertUnrealized(ListBox box, int count)
    {
        var realized = box.GetRealizedContainers().Count();
        Assert.InRange(realized, 1, count - 1);
        Assert.Null(box.ContainerFromIndex(count - 1));
    }

    private static List<Entry> SampleEntries() =>
    [
        FileListSamples.Parent(),
        FileListSamples.File("report.txt", "/t/report.txt", 1_234_567, "rw-r--r--"),
        FileListSamples.Folder("docs", "/t/docs"),
        FileListSamples.File("empty.dat", "/t/empty.dat", 0, "rw-r--r--"),
        FileListSamples.Folder("big", "/t/big", 2048),
        FileListSamples.File("note.txt", "/t/note.txt", 4, "r--r--r--", hidden: true, readOnly: true, link: true),
    ];

    private static void AssertHeaders(FileList list, ILocalizer text)
    {
        Assert.Equal(text.Get("name"), list.HeaderText("name"));
        Assert.Equal(text.Get("ext"), list.HeaderText("ext"));
        Assert.Equal(text.Get("size"), list.HeaderText("size"));
        Assert.Equal(text.Get("date"), list.HeaderText("date"));
        Assert.Equal(text.Get("attr"), list.HeaderText("attr"));
    }

    private static void AssertCells(FileList list, ILocalizer text)
    {
        var name = list.ColumnIndex("name");
        var ext = list.ColumnIndex("ext");
        var size = list.ColumnIndex("size");
        var date = list.ColumnIndex("date");
        var attr = list.ColumnIndex("attr");
        Assert.Equal(text.Get("filelist.parent"), list.CellText(0, name));
        Assert.Equal("", list.CellText(0, size));
        Assert.Equal("", list.CellText(0, date));
        Assert.Equal("report", list.CellText(1, name));
        Assert.Equal("txt", list.CellText(1, ext));
        Assert.Equal(LocalFileSystem.FormatGrouped(1_234_567), list.CellText(1, size));
        Assert.Equal(LocalFileSystem.FormatTime(FileListSamples.Stamp), list.CellText(1, date));
        Assert.Equal("rw-r--r--", list.CellText(1, attr));
        Assert.Equal("docs", list.CellText(2, name));
        Assert.Equal("", list.CellText(2, ext));
        Assert.Equal(text.Get("filelist.directory"), list.CellText(2, size));
        Assert.Equal("0", list.CellText(3, size));
        Assert.Equal(LocalFileSystem.FormatGrouped(2048), list.CellText(4, size));
    }

    private static void AssertFlags(FileList list)
    {
        Assert.True(list.RowHasClass(0, "parent"));
        Assert.True(list.RowHasClass(0, "cursor"));
        Assert.False(list.RowHasClass(0, "directory"));
        Assert.True(list.RowHasClass(2, "directory"));
        Assert.True(list.RowHasClass(5, "hidden"));
        Assert.True(list.RowHasClass(5, "link"));
        Assert.True(list.RowHasClass(5, "readonly"));
        Assert.Equal(0.5, list.RowOpacity(5));
    }

    private static TextBox VisibleEditor(FileList list)
    {
        var editor = list.GetVisualDescendants().OfType<TextBox>().Single(box => box.IsVisible);
        Assert.True(editor.Bounds.Height > 0);
        return editor;
    }
}
