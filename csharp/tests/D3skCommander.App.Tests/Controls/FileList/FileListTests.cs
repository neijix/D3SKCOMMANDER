using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Host;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using Xunit;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Controls.FileLists;

public class FileListTests
{
    [Fact]
    public void OneVirtualizedListServesFullAndBrief()
    {
        var entries = FileListSamples.Files(400);
        FileListHarness.Use(entries, list =>
        {
            var rows = list.Rows;
            AssertUnrealized(list, entries.Count);
            Assert.Equal("f0", list.CellText(0, list.ColumnIndex("name")));
            list.View = FileListView.Brief;
            Assert.Same(rows, list.Rows);
            AssertUnrealized(list, entries.Count);
            Assert.Equal("f0.txt", list.CellText(0, 0));
            Assert.Null(list.HeaderText("ext"));
            Assert.Equal(-1, list.ColumnIndex("size"));
        }, height: 220);
    }

    [Fact]
    public void FullViewShowsTheEntryFields()
    {
        var text = TextCatalog.Load(Language.En, Language.En);
        var entries = SampleEntries();
        FileListHarness.Use(entries, list =>
        {
            AssertHeaders(list, text);
            AssertCells(list, text);
            AssertFlags(list);
            Assert.Equal(list.CellText(1, 0), list.RenderedCells(1)[0]);
            Assert.Equal("txt", list.RenderedCells(1)[1]);
            Assert.Contains(LocalFileSystem.FormatGrouped(1_234_567), list.RenderedCells(1));
        }, localizer: text);
    }

    [Fact]
    public void HeadersFollowTheLanguage()
    {
        var text = TextCatalog.Load(Language.Fr, Language.Fr);
        var entries = new[] { FileListSamples.Folder("docs", "/t/docs") };
        FileListHarness.Use(entries, list =>
        {
            Assert.Equal(text.Get("name"), list.HeaderText("name"));
            Assert.Equal("Nom", list.HeaderText("name"));
            Assert.Equal("Taille", list.HeaderText("size"));
            Assert.NotEqual(text.Get("name", Language.En), list.HeaderText("name"));
            Assert.Equal(text.Get("filelist.directory"), list.CellText(0, list.ColumnIndex("size")));
            Assert.Equal("<Dossier>", list.CellText(0, list.ColumnIndex("size")));
        }, localizer: text);
    }

    [Fact]
    public void APressOnAFileNameSelectsItEvenIfTheMouseMovesOrLingers()
    {
        AssertPressSurvives(1);
        AssertPressSurvives(70);
    }

    [Fact]
    public void ACtrlPressTogglesAndAPlainPressClearsTheSelection()
    {
        var entries = new[]
        {
            FileListSamples.File("five.txt", "/t/five.txt", 5, "rw-r--r--"),
            FileListSamples.File("six.txt", "/t/six.txt", 6, "rw-r--r--"),
        };
        FileListHarness.Use(entries, list =>
        {
            FileListHarness.Press(list, 0, FileListModifiers.Toggle, 1);
            FileListHarness.Press(list, 1, FileListModifiers.Toggle, 1);
            Assert.Equal(2, list.MarkedCount);
            Assert.True(list.IsMarked("/t/five.txt"));
            Assert.True(list.IsMarked("/t/six.txt"));
            FileListHarness.Press(list, 0, FileListModifiers.Toggle, 1);
            Assert.Equal(1, list.MarkedCount);
            Assert.False(list.IsMarked("/t/five.txt"));
            Assert.True(list.IsMarked("/t/six.txt"));
            FileListHarness.Press(list, 0, FileListModifiers.None, 1);
            Assert.Equal(0, list.MarkedCount);
            Assert.Equal(0, list.CursorIndex);
            FileListHarness.Press(list, 1, FileListModifiers.None, 70);
            Assert.Equal(0, list.MarkedCount);
            Assert.Equal(1, list.CursorIndex);
        });
    }

    [Fact]
    public void AShiftPressAddsTheRangeAndCtrlSkipsTheParent()
    {
        var entries = new List<Entry> { FileListSamples.Parent("/t") };
        entries.AddRange(FileListSamples.Files(4));
        FileListHarness.Use(entries, list =>
        {
            FileListHarness.Press(list, 0, FileListModifiers.Toggle, 1);
            Assert.Equal(0, list.MarkedCount);
            Assert.Equal(0, list.CursorIndex);
            list.CursorIndex = 1;
            FileListHarness.Press(list, 3, FileListModifiers.Shift, 1);
            Assert.Equal(3, list.MarkedCount);
            Assert.True(list.IsMarked("/t/f0.txt"));
            Assert.True(list.IsMarked("/t/f2.txt"));
            Assert.False(list.IsMarked("/t"));
            Assert.Equal(3, list.CursorIndex);
        });
    }

    [Fact]
    public void InsertAndSpaceMarkTheCursorEntry()
    {
        var entries = new[]
        {
            FileListSamples.File("a.txt", "/t/a.txt", 1, "rw-r--r--"),
            FileListSamples.Folder("docs", "/t/docs"),
            FileListSamples.File("b.txt", "/t/b.txt", 2, "rw-r--r--"),
        };
        FileListHarness.Use(entries, list =>
        {
            var folders = 0;
            list.DirectoryMarked += (_, _) => folders++;
            Assert.Equal(0, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.Insert);
            Assert.True(list.IsMarked("/t/a.txt"));
            Assert.Equal(1, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.Space);
            Assert.True(list.IsMarked("/t/docs"));
            Assert.Equal(1, list.CursorIndex);
            Assert.Equal(1, folders);
            FileListHarness.Key(list, FileListKey.Space);
            Assert.False(list.IsMarked("/t/docs"));
            Assert.Equal(1, folders);
            list.CursorIndex = 2;
            FileListHarness.Key(list, FileListKey.Space);
            Assert.True(list.IsMarked("/t/b.txt"));
            Assert.Equal(2, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.Space);
            Assert.False(list.IsMarked("/t/b.txt"));
            Assert.False(list.Key(FileListKey.Enter, FileListModifiers.Command));
        });
    }

    [Fact]
    public void PageUpAndPageDownMoveByTwentyRows()
    {
        FileListHarness.Use(FileListSamples.Files(50), list =>
        {
            Assert.Equal(0, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.PageDown);
            Assert.Equal(20, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.PageDown);
            Assert.Equal(40, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.PageDown);
            Assert.Equal(49, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.PageUp);
            Assert.Equal(29, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.PageUp);
            Assert.Equal(9, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.PageUp);
            Assert.Equal(0, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.End);
            Assert.Equal(49, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.Home);
            Assert.Equal(0, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.Down);
            Assert.Equal(1, list.CursorIndex);
            FileListHarness.Key(list, FileListKey.Up);
            Assert.Equal(0, list.CursorIndex);
        });
    }

    [Fact]
    public void RenameEditsTheNameInPlace()
    {
        var entries = new[]
        {
            FileListSamples.Parent("/t"),
            FileListSamples.File("report.txt", "/t/report.txt", 4, "rw-r--r--"),
        };
        FileListHarness.Use(entries, list =>
        {
            list.CursorIndex = 0;
            Assert.False(list.BeginRename());
            list.CursorIndex = 1;
            string? committed = null;
            list.RenameCommitted += (_, e) => committed = e.Name;
            Assert.True(list.BeginRename());
            Assert.Equal(1, list.EditorRow);
            Assert.Equal("report.txt", list.RenameText);
            list.RenameText = "";
            list.TypeRename("new.txt");
            Assert.Equal("new.txt", list.RenameText);
            FileListHarness.Key(list, FileListKey.Enter);
            Assert.Equal("new.txt", committed);
            Assert.False(list.IsRenaming);
        });
    }

    [Fact]
    public void EscapeCancelsRenameAndAnInvalidNameStaysOpen()
    {
        var entries = new[] { FileListSamples.File("report.txt", "/t/report.txt", 4, "rw-r--r--") };
        FileListHarness.Use(entries, list =>
        {
            var commits = 0;
            list.RenameCommitted += (_, _) => commits++;
            Assert.True(list.BeginRename());
            Assert.Equal(0, list.EditorRow);
            list.RenameText = "../nope";
            FileListHarness.Key(list, FileListKey.Enter);
            Assert.True(list.IsRenaming);
            Assert.Equal(0, commits);
            FileListHarness.Key(list, FileListKey.Escape);
            Assert.False(list.IsRenaming);
            Assert.Equal(0, commits);
            Assert.Equal("report", list.CellText(0, list.ColumnIndex("name")));
        });
    }

    [Fact]
    public void CustomRowsUseTheSameList()
    {
        var text = TextCatalog.Load(Language.En, Language.En);
        var columns = new FileListColumn[] { new("name", 1, true), new("path", 180) };
        var rows = new[] { new FileListRow("k", ["alpha", "/a"], "alpha", true, false, false, null) };
        FileListHarness.Use(FileListSamples.Files(1), list =>
        {
            var identity = list.Rows;
            list.ShowRows(columns, rows);
            Assert.Same(identity, list.Rows);
            Assert.Equal(text.Get("name"), list.HeaderText("name"));
            Assert.Equal(text.Get("path"), list.HeaderText("path"));
            Assert.Equal("alpha", list.CellText(0, 0));
            Assert.Equal("/a", list.CellText(0, 1));
        }, localizer: text);
    }

    [Fact]
    public void TheListDrawsOneImGuiFrame()
    {
        var list = FileListHarness.Open(FileListSamples.Files(400), FileListView.Full, 220, null);
        ImGuiProbe.Draw(list.Draw);
        Assert.InRange(list.RealizedCount, 1, 399);
    }

    private static void AssertPressSurvives(int frames)
    {
        var entries = new[]
        {
            FileListSamples.File("file number 5 with a rather long name.txt", "/t/n5.txt", 5, "rw-r--r--"),
        };
        FileListHarness.Use(entries, list =>
        {
            FileListHarness.Press(list, 0, FileListModifiers.Toggle, frames);
            Assert.True(list.IsMarked("/t/n5.txt"), "frames " + frames.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(1, list.MarkedCount);
            Assert.Equal(0, list.CursorIndex);
            Assert.True(list.RowHasClass(0, "marked"));
        });
    }

    private static void AssertUnrealized(FileList list, int count)
    {
        Assert.InRange(list.RealizedCount, 1, count - 1);
        Assert.False(list.IsRealized(count - 1));
        Assert.Empty(list.RenderedCells(count - 1));
        Assert.Equal(1, list.RowOpacity(count - 1));
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
}
