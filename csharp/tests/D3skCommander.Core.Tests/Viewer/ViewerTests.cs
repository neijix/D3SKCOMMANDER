using System.Text;
using D3skCommander.Core.Catalog;
using D3skCommander.Core.Common;
using D3skCommander.Core.Viewer;
using D3skCommander.Tests.Shared;
using Xunit;

namespace D3skCommander.Core.Tests.Viewer;

/// <summary>Session rules v0.1 only covered from the lister window: mode, cursor, truncation.</summary>
public class ViewerTests
{
    [Fact]
    public void BinaryFileOpensInHexAndMissingFileShowsAnError()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "b.bin");
        File.WriteAllBytes(path, [0, 1, 2, 255, 16, 17]);
        var binary = FileViewer.Open(path);
        Assert.Equal(ViewerMode.Hex, binary.Mode);
        Assert.Equal(6, binary.Bytes.Length);
        Assert.Null(binary.Error);

        var missingPath = Path.Combine(dir.Path, "none");
        var missing = FileViewer.Open(missingPath);
        Assert.NotNull(missing.Error);
        Assert.Equal(ErrorKind.NotFound, missing.Error.Kind);
    }

    [Fact]
    public void LargeFileIsTruncated()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "big.txt");
        using (var stream = File.Create(path))
        {
            stream.SetLength(ViewerLimits.MaxRead + 10);
        }

        var viewer = FileViewer.Open(path);
        Assert.True(viewer.Truncated);
        Assert.Equal(ViewerLimits.MaxRead, viewer.Bytes.Length);
    }

    [Fact]
    public void ImageIsDetectedFromItsExtension()
    {
        using var dir = new ViewerTemp();
        Assert.True(ViewerFiles.IsImage(Path.Combine(dir.Path, "photo.PNG")));
        Assert.False(ViewerFiles.IsImage(Path.Combine(dir.Path, "diagram.svg")));

        var png = Path.Combine(dir.Path, "photo.PNG");
        File.WriteAllText(png, "not an image");
        var viewer = FileViewer.Open(png);
        Assert.Equal(ViewerMode.Image, viewer.Mode);
        Assert.Equal([ViewerMode.Image, ViewerMode.Hex], viewer.Modes);
        Assert.True(viewer.ImageFit);
        Assert.Equal(0, viewer.Bytes.Length);

        viewer.SetMode(ViewerMode.Hex);
        Assert.Equal("not an image"u8.ToArray(), viewer.Bytes.ToArray());
    }

    [Fact]
    public void EachOfTheSevenImageExtensionsGetsImageAndHexAndTiffDoesNot()
    {
        using var dir = new ViewerTemp();
        foreach (var extension in new[] { "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico", "PNG" })
        {
            var path = Path.Combine(dir.Path, "a." + extension);
            File.WriteAllBytes(path, [1, 2, 3]);
            var viewer = FileViewer.Open(path);
            Assert.Equal(ViewerMode.Image, viewer.Mode);
            Assert.Equal([ViewerMode.Image, ViewerMode.Hex], viewer.Modes);
        }

        foreach (var extension in new[] { "tif", "tiff", "svg" })
        {
            var path = Path.Combine(dir.Path, "a." + extension);
            File.WriteAllBytes(path, [1, 2, 3, 0]);
            Assert.False(ViewerFiles.IsImage(path));
            Assert.NotEqual(ViewerMode.Image, FileViewer.Open(path).Mode);
        }
    }

    [Fact]
    public void DbfOpensAsATable()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "names.dbf");
        File.WriteAllBytes(path, DbfSample.NameFile());
        var viewer = FileViewer.Open(path);
        Assert.Equal(ViewerMode.Table, viewer.Mode);
        Assert.Equal([ViewerMode.Table, ViewerMode.Hex], viewer.Modes);
        var table = viewer.Table;
        Assert.NotNull(table);
        Assert.Equal(["#", "NAME"], table.Columns);
        Assert.Equal([6, 4], table.Widths);
        Assert.Equal("2", table.Note);
        Assert.Equal(["1", "Ann"], table.Rows[0]);
        Assert.Equal(["2 *", "Bob"], table.Rows[1]);
        Assert.Equal([1], table.Filtered("BOB"));

        var broken = Path.Combine(dir.Path, "bad.dbf");
        File.WriteAllText(broken, "nope");
        var failed = FileViewer.Open(broken);
        Assert.Null(failed.Table);
        Assert.NotNull(failed.Error);
        Assert.Equal(ErrorKind.InvalidData, failed.Error.Kind);
    }

    [Fact]
    public void FindMovesTheTextCursor()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(path, "Alpha\nbeta\nALPHA beta\n");
        var viewer = FileViewer.Open(path);
        Assert.Equal(ViewerMode.Text, viewer.Mode);
        Assert.True(viewer.Wrap);

        viewer.FindText = "alpha";
        Assert.True(viewer.Find(forward: true));
        Assert.Equal(0, viewer.TextCursor);
        Assert.True(viewer.Find(forward: true));
        Assert.Equal(2, viewer.TextCursor);
        Assert.Equal(ViewerNotice.None, viewer.Notice);

        viewer.MatchCase = true;
        Assert.False(viewer.Find(forward: false));
        Assert.Equal(ViewerNotice.NotFound, viewer.Notice);
        Assert.Equal(2, viewer.TextCursor);

        viewer.FindText = "";
        Assert.False(viewer.Find(forward: true));
        Assert.Equal(ViewerNotice.None, viewer.Notice);

        viewer.MatchCase = true;
        viewer.FindText = "ALPHA beta";
        Assert.True(viewer.Find(forward: true));
        Assert.Equal(2, viewer.TextCursor);
        Assert.True(viewer.Find(forward: true));
        Assert.Equal(2, viewer.TextCursor);

        var rows = viewer.TextRows(80);
        Assert.Contains(rows, row => row.Line == 2 && row.Current);
    }

    [Fact]
    public void WrapFeedsTheCursorAndClips()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "wrap.txt");
        File.WriteAllText(path, "abcdefg\nxy");
        var viewer = FileViewer.Open(path);
        var wrapped = viewer.TextRows(3);
        Assert.Equal(["abc", "def", "g", "xy"], wrapped.Select(row => row.Text).ToArray());
        Assert.Equal(1, wrapped[0].Number);
        Assert.Null(wrapped[1].Number);
        Assert.Equal(2, wrapped[3].Number);

        viewer.FindText = "xy";
        Assert.True(viewer.Find(forward: true));
        Assert.Equal(1, viewer.TextCursor);
        Assert.Equal(3, viewer.ScrollRow);

        viewer.Wrap = false;
        var line = new string('x', ViewerLimits.MaxLineChars + 5);
        File.WriteAllText(path, line);
        var clipped = FileViewer.Open(path);
        clipped.Wrap = false;
        var plain = clipped.TextRows(40);
        Assert.Single(plain);
        Assert.Equal(ViewerLimits.MaxLineChars + 2, plain[0].Text.EnumerateRunes().Count());
    }

    [Fact]
    public void HexFindSetsTheByteCursor()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(path, "Alpha\nbeta\n");
        var viewer = FileViewer.Open(path);
        viewer.SetMode(ViewerMode.Hex);
        viewer.FindText = "beta";
        Assert.True(viewer.Find(forward: true));
        Assert.NotNull(viewer.HexCursor);
        Assert.True(viewer.HexLineIsCurrent(viewer.HexCursor.Value / ViewerLimits.HexWidth));
        Assert.Contains("beta", viewer.HexLine(viewer.HexCursor.Value / ViewerLimits.HexWidth), StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedCatalogIsTruncatedNotParsed()
    {
        using var dir = new ViewerTemp();
        var root = Path.Combine(dir.Path, "root");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        var catalog = Path.Combine(dir.Path, "disk.d3cat");
        var written = DiskCatalog.Write(root, catalog, CancellationToken.None);
        Assert.True(written.IsSuccess, written.Error?.Message);
        using (var stream = new FileStream(catalog, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            stream.SetLength(ViewerLimits.MaxCatalogSize + 1);
        }

        Assert.Null(ViewerTables.TryCatalog(catalog));
        var viewer = FileViewer.Open(catalog);
        Assert.Null(viewer.Table);
        Assert.NotEqual(ViewerMode.Table, viewer.Mode);
        Assert.True(viewer.Truncated);
        Assert.Equal(ViewerLimits.MaxRead, viewer.Bytes.Length);
    }
}
