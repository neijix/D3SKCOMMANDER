using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Icons;
using D3skCommander.Core.FileKinds;
using D3skCommander.Core.FileSystem;
using Xunit;

namespace D3skCommander.App.Tests.Controls.FileLists;

public class FileListIconTests
{
    // A real 1 x 1 PNG.
    private const string TinyPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [AvaloniaFact]
    public void EveryKindHasItsOwnIconKeyAndColour()
    {
        var kinds = Enum.GetValues<FileKind>();
        Assert.Equal(kinds.Length, FileKindIcons.All.Select(i => i.Key).Distinct().Count());
        Assert.Equal(kinds.Length, FileKindIcons.All.Select(i => i.Colour).Distinct().Count());
    }

    [AvaloniaFact]
    public void FullRowsShowTheirKindIcon()
    {
        var entries = new[]
        {
            FileListSamples.Parent("/t"),
            FileListSamples.Folder("docs", "/t/docs"),
            FileListSamples.File("pic.png", "/t/pic.png", 5, "rw-r--r--"),
            FileListSamples.File("a.txt", "/t/a.txt", 5, "rw-r--r--"),
            FileListSamples.File("Program.cs", "/t/Program.cs", 5, "rw-r--r--"),
        };
        FileListHarness.Use(entries, h =>
        {
            Assert.Equal("parent", h.Control.IconKey(0));
            Assert.Equal("folder", h.Control.IconKey(1));
            Assert.Equal("image", h.Control.IconKey(2));
            Assert.Equal("text", h.Control.IconKey(3));
            Assert.Equal("code", h.Control.IconKey(4));
            Assert.Equal(22, h.Control.RowContainerHeight(2));
        });
    }

    [AvaloniaFact]
    public void BriefRowsShowTheirKindIconToo()
    {
        var entries = new[] { FileListSamples.Folder("docs", "/t/docs"), FileListSamples.File("a.zip", "/t/a.zip", 1, "rw-r--r--") };
        FileListHarness.Use(entries, h =>
        {
            Assert.Equal("folder", h.Control.IconKey(0));
            Assert.Equal("archive", h.Control.IconKey(1));
        }, FileListView.Brief);
    }

    [AvaloniaFact]
    public void ACustomTableRowHasNoIcon()
    {
        FileListHarness.Use([], h =>
        {
            h.List.ShowRows(
                [new FileListColumn("name", 1, true)],
                [new FileListRow("k", ["cell"], "cell", false, false, false, null)]);
            h.Settle();
            Assert.Null(h.Control.IconKey(0));
        });
    }

    [AvaloniaFact]
    public void TheThumbnailViewRealizesOnlyTheVisibleTiles()
    {
        var entries = FileListSamples.Files(3000);
        FileListHarness.Use(entries, h =>
        {
            Assert.True(h.Control.TilesPerLine >= 4);
            Assert.True(h.Control.RealizedTileCount > 0);
            Assert.True(h.Control.RealizedTileCount < 60, "realized " + h.Control.RealizedTileCount);
            Assert.True(h.Control.IsTileRealized(0));
            Assert.False(h.Control.IsTileRealized(2500));
            Assert.Equal("text", h.Control.TileIconKey(0));
            Assert.Equal("f0.txt", h.Control.TileName(0));
        }, FileListView.Thumbs, 260);
    }

    [AvaloniaFact]
    public void AnImageTileShowsItsThumbnailAndOthersTheirIcon()
    {
        var folder = Directory.CreateTempSubdirectory("d3sk-thumbs").FullName;
        try
        {
            var png = Path.Combine(folder, "pic.png");
            File.WriteAllBytes(png, Convert.FromBase64String(TinyPng));
            var entries = new[]
            {
                FileListSamples.Folder("docs", Path.Combine(folder, "docs")),
                FileListSamples.File("pic.png", png, 70, "rw-r--r--"),
                FileListSamples.File("a.txt", Path.Combine(folder, "a.txt"), 1, "rw-r--r--"),
            };
            FileListHarness.Use(entries, h =>
            {
                WaitFor(h, () => h.Control.TileShowsThumbnail(1));
                Assert.True(h.Control.TileShowsThumbnail(1));
                Assert.False(h.Control.TileShowsThumbnail(0));
                Assert.Equal("folder", h.Control.TileIconKey(0));
                Assert.Equal("text", h.Control.TileIconKey(2));
            }, FileListView.Thumbs);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public void ArrowKeysMoveTheCursorByTileAndTheSelectionStillWorks()
    {
        FileListHarness.Use(FileListSamples.Files(40), h =>
        {
            var line = h.Control.TilesPerLine;
            h.Key(Key.Right);
            Assert.Equal(1, h.List.CursorIndex);
            h.Key(Key.Down);
            Assert.Equal(1 + line, h.List.CursorIndex);
            h.Key(Key.Left);
            Assert.Equal(line, h.List.CursorIndex);
            h.Key(Key.Up);
            Assert.Equal(0, h.List.CursorIndex);
            h.Key(Key.Insert);
            Assert.True(h.List.IsMarked("/t/f0.txt"));
            Assert.Equal(1, h.List.CursorIndex);
            h.Key(Key.End);
            h.Settle();
            Assert.Equal(39, h.List.CursorIndex);
            Assert.True(h.Control.IsTileRealized(39));
            Assert.False(h.List.BeginRename());
        }, FileListView.Thumbs, 260);
    }

    [AvaloniaFact]
    public void ThumbnailsKeepOnlyTheLatestFiveHundredAndTwelve()
    {
        var cache = new ThumbnailCache();
        for (var i = 0; i < ThumbnailCache.Capacity + 100; i++)
        {
            cache.Store("k" + i, null);
        }

        Assert.Equal(ThumbnailCache.Capacity, cache.Count);
        Assert.False(cache.TryGet("k0", out _));
        Assert.True(cache.TryGet("k" + (ThumbnailCache.Capacity + 99), out _));
    }

    private static void WaitFor(FileListHarness harness, Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < until)
        {
            Thread.Sleep(20);
            Dispatcher.UIThread.RunJobs();
            harness.Settle();
        }
    }
}
