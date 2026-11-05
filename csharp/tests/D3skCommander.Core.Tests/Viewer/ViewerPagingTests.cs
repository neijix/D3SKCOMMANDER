using System.Text;
using D3skCommander.Core.Common;
using D3skCommander.Core.Viewer;
using Xunit;

namespace D3skCommander.Core.Tests.Viewer;

/// <summary>The paged reader and the whole-file search (row 19): pages tile the file, never split a character, and keep no handle.</summary>
public class ViewerPagingTests
{
    private const long Sparse = 5L * 1024 * 1024 * 1024;
    private const int Page = ViewerLimits.PageSize;

    [Fact]
    public void TextPagesOfASparseFileStartOnLineStartsAtTheStartMiddleAndEnd()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "big.txt");
        var middle = Sparse / 2;
        MakeSparse(path, Sparse, (0, "head\n"), (middle, "x\nmiddle\n"), (Sparse - 8, "one\ntwo\n"));

        var first = Read(path, 0);
        Assert.Equal(0, first.Start);
        Assert.False(first.AtEnd);
        Assert.Equal("head", first.Lines[0].Text);
        Assert.Equal(0, first.Lines[0].Offset);

        var inside = Read(path, middle - 100);
        Assert.Equal(middle + 2, inside.Start);
        Assert.Equal("middle", inside.Lines[0].Text);
        Assert.Equal(middle + 2, inside.Lines[0].Offset);

        var last = Read(path, Sparse + 10);
        Assert.True(last.AtEnd);
        Assert.Equal(Sparse, last.End);
        Assert.Equal("two", last.Lines[^1].Text);
        Assert.Equal(Sparse - 4, last.Lines[^1].Offset);
    }

    [Fact]
    public void HexPagesOfASparseFileAreAlignedRowsAcrossFourGibibytes()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "big.bin");
        MakeSparse(path, Sparse, (Sparse - 3, "end"));

        var inside = ReadPage(path, (3L << 30) + 5, ViewerMode.Hex);
        Assert.Equal(3L << 30, inside.Start);
        Assert.Equal((3L << 30) + Page, inside.End);
        Assert.Equal(Page / 16, inside.HexRows.Count);
        Assert.False(inside.AtEnd);

        var last = ReadPage(path, Sparse, ViewerMode.Hex);
        Assert.True(last.AtEnd);
        Assert.Equal(Sparse - Page, last.Start);
        var row = last.HexRows[^1];
        Assert.Equal(Sparse - 16, row.Offset);
        Assert.StartsWith("13FFFFFF0 ", row.Format(), StringComparison.Ordinal);
        Assert.Contains("|", row.Format(), StringComparison.Ordinal);
        Assert.Equal(row.Offset, last.HexRows[last.HexRowAt(Sparse - 1)].Offset);
    }

    [Fact]
    public void PageStartsAtTheFirstLineStartAtOrAfterTheOffset()
    {
        using var dir = new ViewerTemp();
        var path = Write(dir, "a.txt", "aaa\nbbb\nccc\n"u8.ToArray());

        Assert.Equal(0, Read(path, 0).Start);
        Assert.Equal(4, Read(path, 1).Start);
        Assert.Equal(4, Read(path, 3).Start);
        Assert.Equal(4, Read(path, 4).Start);
        Assert.Equal(8, Read(path, 5).Start);
        var past = Read(path, 5000);
        Assert.Equal(0, past.Start);
        Assert.True(past.AtEnd);
        Assert.Equal(["aaa", "bbb", "ccc"], past.Lines.Select(line => line.Text));
    }

    [Fact]
    public void ConsecutivePagesTileTheFileWithoutSkippingALine()
    {
        using var dir = new ViewerTemp();
        var builder = new StringBuilder();
        for (var i = 0; builder.Length < 3 * Page; i++)
        {
            builder.Append("line number ").Append(i).Append('\n');
        }

        var path = Write(dir, "lines.txt", Encoding.UTF8.GetBytes(builder.ToString()));
        var texts = new List<string>();
        var page = Read(path, 0);
        texts.AddRange(page.Lines.Select(line => line.Text));
        var count = 1;
        while (!page.AtEnd)
        {
            var next = ViewerPages.Read(path, page.End, ViewerMode.Text, ViewerEncoding.Utf8, exact: true).Page!;
            Assert.Equal(page.End, next.Start);
            texts.AddRange(next.Lines.Select(line => line.Text));
            page = next;
            count++;
        }

        Assert.True(count >= 3);
        Assert.Equal(builder.ToString().TrimEnd('\n').Split('\n'), texts);
    }

    [Fact]
    public void AMultiByteCharacterAtThePageEdgeIsNeverSplit()
    {
        using var dir = new ViewerTemp();
        var bytes = new List<byte>(Encoding.UTF8.GetBytes(new string('a', Page - 1)));
        bytes.AddRange(Encoding.UTF8.GetBytes("ébb"));
        var path = Write(dir, "edge.txt", [.. bytes]);

        var first = Read(path, 0);
        Assert.Equal(Page - 1, first.End);
        Assert.Equal(new string('a', Page - 1), first.Lines[0].Text);
        var second = ViewerPages.Read(path, first.End, ViewerMode.Text, ViewerEncoding.Utf8, exact: true).Page!;
        Assert.Equal("ébb", second.Lines[0].Text);
        Assert.True(second.AtEnd);

        var raw = Read(path, Page);
        Assert.Equal(Page + 1, raw.Start);
        Assert.Equal("bb", raw.Lines[0].Text);
    }

    [Fact]
    public void ASurrogatePairAtThePageEdgeOfUtf16TextIsNeverSplit()
    {
        using var dir = new ViewerTemp();
        var bytes = new List<byte> { 0xFF, 0xFE };
        bytes.AddRange(Encoding.Unicode.GetBytes(new string('a', (Page - 4) / 2)));
        bytes.AddRange(Encoding.Unicode.GetBytes("\U0001F600bb"));
        var path = Write(dir, "wide.txt", [.. bytes]);

        var first = ViewerPages.Read(path, 0, ViewerMode.Text, ViewerEncoding.Utf16Le).Page!;
        Assert.Equal(Page - 2, first.End);
        Assert.Equal((Page - 4) / 2, first.Lines[0].Text.Length);
        var second = ViewerPages.Read(path, first.End, ViewerMode.Text, ViewerEncoding.Utf16Le, exact: true).Page!;
        Assert.Equal("\U0001F600bb", second.Lines[0].Text);
        Assert.Equal(ViewerEncoding.Utf16Le, ViewerPages.DetectEncoding(path));
    }

    [Fact]
    public void AMissingFileIsATypedErrorNotAnException()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "none.txt");

        var read = ViewerPages.Read(path, 0, ViewerMode.Text, ViewerEncoding.Utf8);
        Assert.Null(read.Page);
        Assert.Equal(ErrorKind.NotFound, read.Error!.Kind);
        var found = ViewerSearch.Find(path, "x", false, ViewerMode.Text, ViewerEncoding.Utf8, 0, true, null, CancellationToken.None);
        Assert.Equal(ErrorKind.NotFound, found.Error!.Kind);
    }

    [Fact]
    public void PagesKeepNoHandleSoTheFileCanBeDeletedAndRewritten()
    {
        using var dir = new ViewerTemp();
        var path = Write(dir, "w.txt", "one\n"u8.ToArray());
        Assert.Equal("one", Read(path, 0).Lines[0].Text);

        File.WriteAllText(path, "two\n");
        Assert.Equal("two", Read(path, 0).Lines[0].Text);
        File.Delete(path);
    }

    [Fact]
    public void SearchFindsAMatchAcrossAChunkEdgeForwardAndBackward()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "f.bin");
        var at = Page - 3;
        MakeSparse(path, (2L * Page) + 500, (at, "needle"));

        var forward = Find(path, "NEEDLE", false, 0, true);
        Assert.Equal(new ViewerMatch(at, 6), forward);
        Assert.Null(Find(path, "NEEDLE", true, 0, true));
        Assert.Equal(new ViewerMatch(at, 6), Find(path, "needle", true, (2L * Page) + 500, false));
        Assert.Equal(new ViewerMatch(at, 6), Find(path, "needle", true, at + 1, true));
        Assert.Equal(new ViewerMatch(at, 6), Find(path, "needle", true, at, false));
        Assert.Null(Find(path, "absent", false, 0, true));
    }

    [Fact]
    public void SearchPastFourGibibytesFindsTextInUtf16AndStopsWhenCancelled()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "u.txt");
        var at = (4L << 30) + 10;
        var bytes = Encoding.Unicode.GetBytes("Found");
        MakeSparse(path, (4L << 30) + 4096);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            stream.Position = at;
            stream.Write(bytes);
        }

        var match = ViewerSearch.Find(path, "found", false, ViewerMode.Text, ViewerEncoding.Utf16Le, 4L << 30, true, null, CancellationToken.None);
        Assert.Equal(new ViewerMatch(at, 10), match.Match);
        var odd = ViewerSearch.Find(path, "ound", false, ViewerMode.Text, ViewerEncoding.Utf16Le, 0, false, null, CancellationToken.None);
        Assert.Equal(at + 2, odd.Match?.Offset);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            ViewerSearch.Find(path, "found", false, ViewerMode.Text, ViewerEncoding.Utf16Le, 0, true, null, cancelled.Token));
    }

    private static ViewerPage Read(string path, long offset) => ReadPage(path, offset, ViewerMode.Text);

    private static ViewerPage ReadPage(string path, long offset, ViewerMode mode)
    {
        var read = ViewerPages.Read(path, offset, mode, ViewerEncoding.Utf8);
        Assert.Null(read.Error);
        return read.Page!;
    }

    private static ViewerMatch? Find(string path, string query, bool matchCase, long from, bool forward)
    {
        var found = ViewerSearch.Find(path, query, matchCase, ViewerMode.Hex, ViewerEncoding.Utf8, from, forward, null, CancellationToken.None);
        Assert.Null(found.Error);
        return found.Match;
    }

    private static string Write(ViewerTemp dir, string name, byte[] bytes)
    {
        var path = Path.Combine(dir.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void MakeSparse(string path, long length, params (long Offset, string Text)[] texts)
    {
        using var stream = new FileStream(path, FileMode.Create);
        stream.SetLength(length);
        foreach (var (offset, text) in texts)
        {
            stream.Position = offset;
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
    }
}
