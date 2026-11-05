using Avalonia.Headless.XUnit;
using Avalonia.Input;
using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using Xunit;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Controls.FileLists;

public class FileListTests
{
    private const RawInputModifiers Ctrl = RawInputModifiers.Control;
    private const RawInputModifiers Shift = RawInputModifiers.Shift;

    [AvaloniaFact]
    public void OneVirtualizedListServesFullAndBrief()
    {
        var entries = FileListSamples.Files(400);
        FileListHarness.Use(entries, h =>
        {
            var rows = h.List.Rows;
            AssertUnrealized(h, entries.Count);
            Assert.Equal("f0", h.List.CellText(0, h.List.ColumnIndex("name")));
            h.List.View = FileListView.Brief;
            h.Settle();
            Assert.Same(rows, h.List.Rows);
            AssertUnrealized(h, entries.Count);
            Assert.Equal("f0.txt", h.List.CellText(0, 0));
            Assert.Null(h.Control.HeaderText("ext"));
            Assert.Equal(-1, h.List.ColumnIndex("size"));
        }, height: 220);
    }

    [AvaloniaFact]
    public void FullViewShowsTheEntryFields()
    {
        var text = TextCatalog.Load(Language.En, Language.En);
        var entries = SampleEntries();
        FileListHarness.Use(entries, h =>
        {
            AssertHeaders(h, text);
            AssertCells(h.List, text);
            AssertFlags(h);
            Assert.Equal(h.List.CellText(1, 0), h.Control.RenderedCells(1)[0]);
            Assert.Equal("txt", h.Control.RenderedCells(1)[1]);
            Assert.Contains(LocalFileSystem.FormatGrouped(1_234_567), h.Control.RenderedCells(1));
        }, localizer: text);
    }

    [AvaloniaFact]
    public void HeadersFollowTheLanguage()
    {
        var text = TextCatalog.Load(Language.Fr, Language.Fr);
        var entries = new[] { FileListSamples.Folder("docs", "/t/docs") };
        FileListHarness.Use(entries, h =>
        {
            Assert.Equal(text.Get("name"), h.Control.HeaderText("name"));
            Assert.Equal("Nom", h.Control.HeaderText("name"));
            Assert.Equal("Taille", h.Control.HeaderText("size"));
            Assert.NotEqual(text.Get("name", Language.En), h.Control.HeaderText("name"));
            Assert.Equal(text.Get("filelist.directory"), h.List.CellText(0, h.List.ColumnIndex("size")));
            Assert.Equal("<Dossier>", h.Control.RenderedCells(0)[h.List.ColumnIndex("size")]);
        }, localizer: text);
    }

    [AvaloniaFact]
    public void APressOnAFileNameSelectsItEvenIfTheMouseMovesOrLingers()
    {
        AssertPressSurvives(0);
        AssertPressSurvives(70);
    }

    [AvaloniaFact]
    public void ACtrlPressTogglesAndAPlainPressClearsTheSelection()
    {
        var entries = new[]
        {
            FileListSamples.File("five.txt", "/t/five.txt", 5, "rw-r--r--"),
            FileListSamples.File("six.txt", "/t/six.txt", 6, "rw-r--r--"),
        };
        FileListHarness.Use(entries, h =>
        {
            h.Press(0, Ctrl);
            h.Press(1, Ctrl);
            Assert.Equal(2, h.List.MarkedCount);
            Assert.True(h.List.IsMarked("/t/five.txt"));
            Assert.True(h.List.IsMarked("/t/six.txt"));
            h.Press(0, Ctrl);
            Assert.Equal(1, h.List.MarkedCount);
            Assert.False(h.List.IsMarked("/t/five.txt"));
            Assert.True(h.List.IsMarked("/t/six.txt"));
            h.Press(0);
            Assert.Equal(0, h.List.MarkedCount);
            Assert.Equal(0, h.List.CursorIndex);
            h.Press(1, moves: 70);
            Assert.Equal(0, h.List.MarkedCount);
            Assert.Equal(1, h.List.CursorIndex);
        });
    }

    [AvaloniaFact]
    public void AShiftPressAddsTheRangeAndCtrlSkipsTheParent()
    {
        var entries = new List<Entry> { FileListSamples.Parent("/t") };
        entries.AddRange(FileListSamples.Files(4));
        FileListHarness.Use(entries, h =>
        {
            h.Press(0, Ctrl);
            Assert.Equal(0, h.List.MarkedCount);
            Assert.Equal(0, h.List.CursorIndex);
            h.List.CursorIndex = 1;
            h.Press(3, Shift);
            Assert.Equal(3, h.List.MarkedCount);
            Assert.True(h.List.IsMarked("/t/f0.txt"));
            Assert.True(h.List.IsMarked("/t/f2.txt"));
            Assert.False(h.List.IsMarked("/t"));
            Assert.Equal(3, h.List.CursorIndex);
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
        FileListHarness.Use(entries, h =>
        {
            var folders = 0;
            h.List.DirectoryMarked += (_, _) => folders++;
            Assert.Equal(0, h.List.CursorIndex);
            h.Key(Key.Insert);
            Assert.True(h.List.IsMarked("/t/a.txt"));
            Assert.Equal(1, h.List.CursorIndex);
            h.Key(Key.Space);
            Assert.True(h.List.IsMarked("/t/docs"));
            Assert.Equal(1, h.List.CursorIndex);
            Assert.Equal(1, folders);
            h.Key(Key.Space);
            Assert.False(h.List.IsMarked("/t/docs"));
            Assert.Equal(1, folders);
            h.List.CursorIndex = 2;
            h.Key(Key.Space);
            Assert.True(h.List.IsMarked("/t/b.txt"));
            Assert.Equal(2, h.List.CursorIndex);
            h.Key(Key.Space);
            Assert.False(h.List.IsMarked("/t/b.txt"));
            Assert.True(h.Control.RowHasClass(0, "marked"));
        });
    }

    [AvaloniaFact]
    public void TheCommandModifierIsIgnored()
    {
        FileListHarness.Use(FileListSamples.Files(5), h =>
        {
            h.Key(Key.Insert, RawInputModifiers.Meta);
            h.Key(Key.Down, RawInputModifiers.Meta);
            Assert.Equal(0, h.List.MarkedCount);
            Assert.Equal(0, h.List.CursorIndex);
        });
    }

    [AvaloniaFact]
    public void ShiftWithAMoveTogglesEachRowPassed()
    {
        FileListHarness.Use(FileListSamples.Files(6), h =>
        {
            h.Key(Key.Down, Shift);
            h.Key(Key.Down, Shift);
            Assert.Equal(2, h.List.MarkedCount);
            Assert.Equal(2, h.List.CursorIndex);
            h.Key(Key.Up, Shift);
            Assert.Equal(1, h.List.CursorIndex);
            Assert.Equal(3, h.List.MarkedCount);
            Assert.True(h.List.IsMarked("/t/f0.txt"));
            Assert.True(h.List.IsMarked("/t/f1.txt"));
            Assert.True(h.List.IsMarked("/t/f2.txt"));
        });
    }

    [AvaloniaFact]
    public void PageUpAndPageDownMoveByTwentyRows()
    {
        FileListHarness.Use(FileListSamples.Files(50), h =>
        {
            Assert.Equal(0, h.List.CursorIndex);
            h.Key(Key.PageDown);
            Assert.Equal(20, h.List.CursorIndex);
            h.Key(Key.PageDown);
            Assert.Equal(40, h.List.CursorIndex);
            h.Key(Key.PageDown);
            Assert.Equal(49, h.List.CursorIndex);
            h.Key(Key.PageUp);
            Assert.Equal(29, h.List.CursorIndex);
            h.Key(Key.PageUp);
            Assert.Equal(9, h.List.CursorIndex);
            h.Key(Key.PageUp);
            Assert.Equal(0, h.List.CursorIndex);
            h.Key(Key.End);
            Assert.Equal(49, h.List.CursorIndex);
            h.Key(Key.Home);
            Assert.Equal(0, h.List.CursorIndex);
            h.Key(Key.Down);
            Assert.Equal(1, h.List.CursorIndex);
            h.Key(Key.Up);
            Assert.Equal(0, h.List.CursorIndex);
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
        FileListHarness.Use(entries, h =>
        {
            h.List.CursorIndex = 0;
            Assert.False(h.List.BeginRename());
            h.List.CursorIndex = 1;
            string? committed = null;
            h.List.RenameCommitted += (_, e) => committed = e.Name;
            Assert.True(h.List.BeginRename());
            h.Settle();
            Assert.Equal(1, h.List.EditorRow);
            var editor = h.Control.EditorAt(1);
            Assert.NotNull(editor);
            Assert.Equal("report.txt", editor.Text);
            editor.Text = "new.txt";
            Assert.Equal("new.txt", h.List.RenameText);
            h.Key(Key.Enter);
            Assert.Equal("new.txt", committed);
            Assert.False(h.List.IsRenaming);
            Assert.Null(h.Control.EditorAt(1));
        });
    }

    [AvaloniaFact]
    public void EscapeCancelsRenameAndAnInvalidNameStaysOpen()
    {
        var entries = new[] { FileListSamples.File("report.txt", "/t/report.txt", 4, "rw-r--r--") };
        FileListHarness.Use(entries, h =>
        {
            var commits = 0;
            h.List.RenameCommitted += (_, _) => commits++;
            Assert.True(h.List.BeginRename());
            h.Settle();
            Assert.Equal(0, h.List.EditorRow);
            h.Control.EditorAt(0)!.Text = "../nope";
            h.Key(Key.Enter);
            Assert.True(h.List.IsRenaming);
            Assert.Equal(0, commits);
            h.Key(Key.Escape);
            Assert.False(h.List.IsRenaming);
            Assert.Equal(0, commits);
            Assert.Equal("report", h.List.CellText(0, h.List.ColumnIndex("name")));
        });
    }

    [AvaloniaFact]
    public void APressCancelsARunningRename()
    {
        var entries = FileListSamples.Files(3);
        FileListHarness.Use(entries, h =>
        {
            Assert.True(h.List.BeginRename());
            h.Settle();
            h.Press(2);
            Assert.False(h.List.IsRenaming);
            Assert.Equal(2, h.List.CursorIndex);
        });
    }

    [AvaloniaFact]
    public void CustomRowsUseTheSameList()
    {
        var text = TextCatalog.Load(Language.En, Language.En);
        var columns = new FileListColumn[] { new("name", 1, true), new("path", 180) };
        var rows = new[] { new FileListRow("k", ["alpha", "/a"], "alpha", true, false, false, null) };
        FileListHarness.Use(FileListSamples.Files(1), h =>
        {
            var identity = h.List.Rows;
            h.List.ShowRows(columns, rows);
            h.Settle();
            Assert.Same(identity, h.List.Rows);
            Assert.Equal(text.Get("name"), h.Control.HeaderText("name"));
            Assert.Equal(text.Get("path"), h.Control.HeaderText("path"));
            Assert.Equal("alpha", h.List.CellText(0, 0));
            Assert.Equal(["alpha", "/a"], h.Control.RenderedCells(0));
        }, localizer: text);
    }

    [AvaloniaFact]
    public void TheListRendersOneHeadlessFrame()
    {
        FileListHarness.Use(FileListSamples.Files(1000), h =>
        {
            Assert.InRange(h.Control.RealizedCount, 1, 100);
            Assert.True(h.Control.IsRealized(0));
            Assert.False(h.Control.IsRealized(999));
        }, height: 220);
    }

    [AvaloniaFact]
    public void AClickPastTheNameSelectsTheRow()
    {
        var entries = new[]
        {
            FileListSamples.File("a.txt", "/t/a.txt", 1, "rw-r--r--"),
            FileListSamples.File("b.txt", "/t/b.txt", 2, "rw-r--r--"),
        };
        FileListHarness.Use(entries, h =>
        {
            h.Press(1);
            Assert.Equal(1, h.List.CursorIndex);
            Assert.Equal(0, h.List.MarkedCount);
        }, height: 220);
    }

    [AvaloniaFact]
    public void ADoubleClickOnTheRowActivatesIt()
    {
        var entries = new[]
        {
            FileListSamples.File("a.txt", "/t/a.txt", 1, "rw-r--r--"),
            FileListSamples.Folder("docs", "/t/docs"),
        };
        FileListHarness.Use(entries, h =>
        {
            var opened = -1;
            h.Control.RowActivated += (_, index) => opened = index;
            h.DoubleClick(1);
            Assert.Equal(1, opened);
            Assert.Equal(1, h.List.CursorIndex);
        }, height: 220);
    }

    [AvaloniaFact]
    public void TheCursorStaysOnScreenAfterALongJump()
    {
        FileListHarness.Use(FileListSamples.Files(400), h =>
        {
            h.Key(Key.End);
            Assert.Equal(399, h.List.CursorIndex);
            Assert.True(h.Control.IsRealized(399));
            Assert.False(h.Control.IsRealized(0));
            h.Key(Key.Home);
            Assert.True(h.Control.IsRealized(0));
        }, height: 220);
    }

    [AvaloniaFact]
    public void TheWheelMovesALongFolder()
    {
        FileListHarness.Use(FileListSamples.Files(400), h =>
        {
            Assert.True(h.Control.IsRealized(0));
            h.Wheel(0, -40);
            Assert.False(h.Control.IsRealized(0));
            var later = Enumerable.Range(15, 385).Any(h.Control.IsRealized);
            Assert.True(later);
        }, height: 220);
    }

    private static void AssertPressSurvives(int moves)
    {
        var entries = new[]
        {
            FileListSamples.File("file number 5 with a rather long name.txt", "/t/n5.txt", 5, "rw-r--r--"),
        };
        FileListHarness.Use(entries, h =>
        {
            h.Press(0, Ctrl, moves);
            Assert.True(h.List.IsMarked("/t/n5.txt"), "moves " + moves.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(1, h.List.MarkedCount);
            Assert.Equal(0, h.List.CursorIndex);
            Assert.True(h.Control.RowHasClass(0, "marked"));
        });
    }

    private static void AssertUnrealized(FileListHarness h, int count)
    {
        Assert.InRange(h.Control.RealizedCount, 1, count - 1);
        Assert.False(h.Control.IsRealized(count - 1));
        Assert.Empty(h.Control.RenderedCells(count - 1));
        Assert.Equal(1, h.Control.RowOpacity(count - 1));
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

    private static void AssertHeaders(FileListHarness h, ILocalizer text)
    {
        var list = h.Control;
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

    private static void AssertFlags(FileListHarness h)
    {
        var list = h.Control;
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
